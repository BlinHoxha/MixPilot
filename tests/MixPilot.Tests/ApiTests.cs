using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using MixPilot.Contracts;

namespace MixPilot.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private HttpClient Client() => factory.Client(Guid.NewGuid().ToString());
    private static CreateTrackRequest Track(string title = "Sunrise") => new()
    { Artist = "Test Artist", Title = title, DurationSeconds = 300, Bpm = 124, Energy = 5, MusicalKey = "8A", Genre = "House", AuthorizationConfirmed = true };
    private static async Task<T> Data<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<CommonResult<T>>(Json))!.Data!;
    }
    private static async Task<string> Code(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>();

    [Fact]
    public async Task Library_crud_pagination_duplicate_and_revision_contract()
    {
        using var client = Client();
        var created = await client.PostAsJsonAsync("/api/v1/tracks", Track());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); Assert.NotNull(created.Headers.Location);
        var track = await Data<TrackResponse>(created);
        Assert.True(track.Rights.CanReference); Assert.False(track.Rights.CanMix); Assert.False(track.Rights.CanAnalyze);
        var duplicate = await client.PostAsJsonAsync("/api/v1/tracks", Track());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode); Assert.Equal("duplicate_track", await Code(duplicate));
        await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", Track("Evening")));
        var page = await Data<PageResponse<TrackResponse>>(await client.GetAsync("/api/v1/tracks?pageSize=1&sortBy=title&direction=asc"));
        Assert.Single(page.Items); Assert.Equal(2, page.TotalCount); Assert.Equal(2, page.TotalPages);
        Assert.Equal("Evening", page.Items.Single().Title);
        var request = new UpdateTrackRequest { Artist = "Test Artist", Title = "Corrected", DurationSeconds = 300, ExpectedRevision = 1 };
        var updated = await Data<TrackResponse>(await client.PutAsJsonAsync($"/api/v1/tracks/{track.Id}", request));
        Assert.Equal(2, updated.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/tracks/{track.Id}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/tracks/{track.Id}?expectedRevision=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/tracks/{track.Id}")).StatusCode);
    }

    [Fact]
    public async Task Authentication_and_owner_boundaries_cover_reads_mutations_and_relationships()
    {
        using var anonymous = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/tracks")).StatusCode);
        using var alice = Client(); using var bob = Client();
        var track = await Data<TrackResponse>(await alice.PostAsJsonAsync("/api/v1/tracks", Track()));
        var collection = await Data<CollectionResponse>(await alice.PostAsJsonAsync("/api/v1/collections", new SaveCollectionRequest { Name = "Set prep", TrackIds = [track.Id] }));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/tracks/{track.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/v1/tracks/{track.Id}?expectedRevision=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/collections/{collection.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/v1/collections", new SaveCollectionRequest { Name = "Attempt", TrackIds = [track.Id] })).StatusCode);
        Assert.Empty((await Data<PageResponse<TrackResponse>>(await bob.GetAsync("/api/v1/tracks"))).Items);
    }

    [Fact]
    public async Task Invalid_payloads_and_rights_overposting_use_problem_details()
    {
        using var client = Client();
        var invalid = await client.PostAsJsonAsync("/api/v1/tracks", new { artist = "", title = "", durationSeconds = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
        Assert.Equal("validation_failed", await Code(invalid));
        var rights = await client.PostAsJsonAsync("/api/v1/tracks", new { artist = "A", title = "B", durationSeconds = 200, authorizationConfirmed = true, rights = new { canMix = true } });
        Assert.Equal(HttpStatusCode.BadRequest, rights.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/tracks?pageSize=101")).StatusCode);
        var inverted = await client.PostAsJsonAsync("/api/v1/sets/generate", new { name = "Invalid", requirements = new { minBpm = 140, maxBpm = 120 } });
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        var numericEnum = await client.PostAsJsonAsync("/api/v1/sets/generate", new { name = "Invalid", requirements = new { mode = 999 } });
        Assert.Equal(HttpStatusCode.BadRequest, numericEnum.StatusCode);
    }

    [Fact]
    public async Task Csv_is_atomic_handles_quoted_fields_and_skips_duplicates()
    {
        using var client = Client();
        const string csv = "artist,title,durationSeconds,bpm,musicalKey,energy\n\"Artist, One\",Sunrise,300,124,8A,5\nArtist Two,Sunset,300,125,9A,6";
        var imported = await Data<ImportResponse>(await client.PostAsJsonAsync("/api/v1/imports/csv", new CsvImportRequest { Csv = csv, AuthorizationConfirmed = true }));
        Assert.Equal(2, imported.CreatedCount);
        var repeated = await Data<ImportResponse>(await client.PostAsJsonAsync("/api/v1/imports/csv", new CsvImportRequest { Csv = csv, AuthorizationConfirmed = true }));
        Assert.Equal(2, repeated.SkippedDuplicateCount); Assert.Equal(0, repeated.CreatedCount);
        var invalid = await client.PostAsJsonAsync("/api/v1/imports/csv", new CsvImportRequest { AuthorizationConfirmed = true, Csv = "artist,title,durationSeconds\nNew,Valid,300\nNew,Invalid,nope" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(2, (await Data<PageResponse<TrackResponse>>(await client.GetAsync("/api/v1/tracks"))).TotalCount);
    }

    [Fact]
    public async Task Sets_generate_edit_regenerate_preserve_locks_versions_and_isolation()
    {
        using var client = Client();
        for (int i = 0; i < 8; i++) await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", Track($"Track {i}")));
        var requirements = new SetRequirementsRequest { DurationSeconds = 900 };
        var generated = await Data<SetResponse>(await client.PostAsJsonAsync("/api/v1/sets/generate", new GenerateSetRequest { Name = "Practice", Requirements = requirements }, Json));
        Assert.Equal(3, generated.Tracks.Count); Assert.Equal(900, generated.DurationSeconds);
        var reordered = generated.Tracks.Reverse().Select(t => t.TrackId).ToArray();
        var edited = await Data<SetResponse>(await client.PutAsJsonAsync($"/api/v1/sets/{generated.Id}/tracks", new EditSetRequest
        { ExpectedRevision = 1, OrderedTrackIds = reordered, LockedTrackIds = [reordered[1]] }));
        Assert.Equal(2, edited.Revision);
        var regenerated = await Data<SetResponse>(await client.PostAsJsonAsync($"/api/v1/sets/{generated.Id}/regenerate", new RegenerateSetRequest { ExpectedRevision = 2, Requirements = requirements }, Json));
        Assert.Equal(reordered[1], regenerated.Tracks.Single(t => t.Position == 2).TrackId);
        Assert.True(regenerated.Tracks.Single(t => t.Position == 2).IsLocked); Assert.Equal(3, regenerated.Version);
        var original = await Data<SetResponse>(await client.GetAsync($"/api/v1/sets/{generated.Id}/versions/1"));
        Assert.Equal(generated.Tracks.Select(t => t.TrackId), original.Tracks.Select(t => t.TrackId));
        Assert.Equal(3, (await Data<SetVersionResponse[]>(await client.GetAsync($"/api/v1/sets/{generated.Id}/versions"))).Length);
        var stale = await client.PostAsJsonAsync($"/api/v1/sets/{generated.Id}/regenerate", new RegenerateSetRequest { ExpectedRevision = 2, Requirements = requirements }, Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var other = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/sets/{generated.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/sets/{generated.Id}/versions/1")).StatusCode);
        var mix = await client.PostAsJsonAsync("/api/v1/sets/generate", new GenerateSetRequest { Name = "Mix", Requirements = new() { Mode = PlanningMode.Mixable } }, Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mix.StatusCode);
    }

    [Fact]
    public async Task Profile_and_collection_edits_are_revisioned()
    {
        using var client = Client();
        var profile = await Data<ProfileResponse>(await client.PutAsJsonAsync("/api/v1/me/profile", new SaveProfileRequest { DisplayName = "DJ Test" }));
        Assert.Equal(1, profile.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/v1/me/profile", new SaveProfileRequest { DisplayName = "Stale" })).StatusCode);
        var first = await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", Track("First")));
        var second = await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", Track("Second")));
        var collection = await Data<CollectionResponse>(await client.PostAsJsonAsync("/api/v1/collections", new SaveCollectionRequest { Name = "Prep", TrackIds = [first.Id, second.Id] }));
        var updated = await Data<CollectionResponse>(await client.PutAsJsonAsync($"/api/v1/collections/{collection.Id}", new SaveCollectionRequest { Name = "Updated", ExpectedRevision = 1, TrackIds = [second.Id, first.Id] }));
        Assert.Equal(2, updated.Revision); Assert.Equal(second.Id, updated.TrackIds[0]);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/tracks/{first.Id}?expectedRevision=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/collections/{collection.Id}?expectedRevision=2")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_contains_shared_contracts_and_string_enums()
    {
        using var client = Client();
        var schema = await client.GetFromJsonAsync<JsonObject>("/swagger/v1/swagger.json");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MixPilot.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string contractPath = Path.Combine(root.FullName, "docs", "openapi.json");
        if (Environment.GetEnvironmentVariable("MIXPILOT_EXPORT_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contractPath)!);
            await File.WriteAllTextAsync(contractPath, schema!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.True(File.Exists(contractPath), "Export the OpenAPI contract using scripts/Export-OpenApi.ps1.");
        Assert.True(JsonNode.DeepEquals(schema, JsonNode.Parse(await File.ReadAllTextAsync(contractPath))), "OpenAPI drift detected; review and re-export the contract.");
        Assert.NotNull(schema!["paths"]!["/api/v1/tracks"]);
        Assert.NotNull(schema["components"]!["schemas"]!["TrackResponse"]);
        Assert.Contains("planning", schema["components"]!["schemas"]!["PlanningMode"]!["enum"]!.ToJsonString());
        var capabilities = await Data<CapabilitiesResponse>(await client.GetAsync("/api/v1/capabilities"));
        Assert.False(capabilities.PromptInterpretationAvailable); Assert.False(capabilities.AudioOperationsAvailable);
    }
}
