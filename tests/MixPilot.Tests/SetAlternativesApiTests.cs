using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Framework.Contracts.Common;
using MixPilot.Contracts;

namespace MixPilot.Tests;

public sealed class SetAlternativesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private HttpClient Client() => factory.Client(Guid.NewGuid().ToString());
    private static async Task<T> Data<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        return (await response.Content.ReadFromJsonAsync<CommonResult<T>>(Json))!.Data!;
    }

    private static async Task<(SetResponse Set, TrackResponse[] Tracks)> CreateSet(HttpClient client, int count = 16)
    {
        var tracks = new List<TrackResponse>();
        for (int i = 0; i < count; i++)
            tracks.Add(await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
            {
                Artist = "Alternatives DJ", Title = $"Candidate {i}", Version = "Original", DurationSeconds = 300,
                Bpm = 124, Energy = 5, MusicalKey = "8A", Genre = "House", AuthorizationConfirmed = true
            })));
        var set = await Data<SetResponse>(await client.PostAsJsonAsync("/api/v1/sets/generate", new GenerateSetRequest
        { Name = "Alternatives set", Requirements = new() { DurationSeconds = 900, Genre = "House" } }, Json));
        return (set, tracks.ToArray());
    }

    [Fact]
    public async Task Suggestions_are_ranked_owner_scoped_read_only_and_advertised_in_capabilities()
    {
        using var client = Client();
        var (set, tracks) = await CreateSet(client);
        var root = $"/api/v1/sets/{set.Id}";
        var before = await Data<SetResponse>(await client.GetAsync(root));
        var response = await client.GetAsync($"{root}/positions/2/alternatives");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var alternatives = await Data<SetAlternativesResponse>(response);
        Assert.Equal(set.Id, alternatives.SetId);
        Assert.Equal(set.Version, alternatives.Version);
        Assert.Equal(2, alternatives.Position);
        Assert.Equal(set.Tracks[1].TrackId, alternatives.CurrentTrackId);
        Assert.Equal(10, alternatives.Items.Count);
        var used = set.Tracks.Select(t => t.TrackId).ToHashSet();
        var eligible = tracks.Where(t => !used.Contains(t.Id)).OrderBy(t => t.Id).ToArray();
        Assert.Equal(eligible.Take(10).Select(t => t.Id), alternatives.Items.Select(t => t.TrackId));
        Assert.All(alternatives.Items, item =>
        {
            Assert.DoesNotContain(item.TrackId, used);
            Assert.Equal("Alternatives DJ", item.Artist);
            Assert.StartsWith("Candidate ", item.Title);
            Assert.Equal("Original", item.Version);
            Assert.Equal(300, item.DurationSeconds);
            Assert.Equal(124m, item.Bpm);
            Assert.Equal("8A", item.MusicalKey);
            Assert.Equal(5, item.Energy);
            Assert.Equal("House", item.Genre);
            Assert.Equal(1, item.Score);
            Assert.Equal(1, item.Transition);
            Assert.Equal(1, item.EnergyFit);
            Assert.Equal(1, item.DurationFit);
            Assert.InRange(item.Reasons.Count, 2, 4);
        });
        var limited = await Data<SetAlternativesResponse>(await client.GetAsync($"{root}/positions/2/alternatives?limit=1"));
        Assert.Single(limited.Items);
        Assert.Equal(alternatives.Items[0].TrackId, limited.Items[0].TrackId);
        Assert.Equal(alternatives.Items[0].Reasons, limited.Items[0].Reasons);
        var all = await Data<SetAlternativesResponse>(await client.GetAsync($"{root}/positions/2/alternatives?limit=25"));
        Assert.Equal(13, all.Items.Count);
        var again = await Data<SetAlternativesResponse>(await client.GetAsync($"{root}/positions/2/alternatives"));
        Assert.Equal(alternatives.Items.Select(t => t.TrackId), again.Items.Select(t => t.TrackId));
        var unchanged = await Data<SetResponse>(await client.GetAsync(root));
        Assert.Equal(before.Revision, unchanged.Revision);
        Assert.Equal(before.Version, unchanged.Version);
        Assert.Equal(before.UpdatedAt, unchanged.UpdatedAt);
        Assert.Equal(before.Tracks.Select(t => t.TrackId), unchanged.Tracks.Select(t => t.TrackId));
        Assert.Single(await Data<SetVersionResponse[]>(await client.GetAsync($"{root}/versions")));
        var capabilities = await Data<CapabilitiesResponse>(await client.GetAsync("/api/v1/capabilities"));
        Assert.Contains("setAlternatives", capabilities.EnabledFeatures);
    }

    [Fact]
    public async Task Unknown_unowned_missing_and_locked_positions_follow_the_error_contract()
    {
        using var client = Client();
        using var other = Client();
        using var anonymous = factory.Client();
        var (set, _) = await CreateSet(client, 4);
        var root = $"/api/v1/sets/{set.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{root}/positions/1/alternatives")).StatusCode);
        foreach (var (caller, url) in new[]
        {
            (other, $"{root}/positions/1/alternatives"),
            (client, $"/api/v1/sets/{Guid.NewGuid()}/positions/1/alternatives"),
            (client, $"{root}/positions/4/alternatives")
        })
        {
            var missing = await caller.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("not_found", (await missing.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        }
        var locked = await Data<SetResponse>(await client.PutAsJsonAsync($"{root}/tracks", new EditSetRequest
        {
            ExpectedRevision = set.Revision, OrderedTrackIds = set.Tracks.Select(t => t.TrackId).Reverse().ToArray(),
            LockedTrackIds = [set.Tracks[0].TrackId]
        }));
        var lockedPosition = locked.Tracks.Single(t => t.IsLocked).Position;
        var rejected = await client.GetAsync($"{root}/positions/{lockedPosition}/alternatives");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType!.MediaType);
        Assert.Equal("position_locked", (await rejected.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        var newest = await Data<SetAlternativesResponse>(await client.GetAsync($"{root}/positions/1/alternatives"));
        Assert.Equal(2, newest.Version);
        Assert.Equal(locked.Tracks[0].TrackId, newest.CurrentTrackId);
        Assert.Equal(locked.Revision, (await Data<SetResponse>(await client.GetAsync(root))).Revision);
        Assert.Equal(2, (await Data<SetVersionResponse[]>(await client.GetAsync($"{root}/versions"))).Length);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("26")]
    [InlineData("invalid")]
    public async Task Invalid_limits_return_validation_problem_details(string limit)
    {
        using var client = Client();
        var (set, _) = await CreateSet(client, 3);
        var response = await client.GetAsync($"/api/v1/sets/{set.Id}/positions/1/alternatives?limit={limit}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task No_eligible_candidates_returns_empty_items()
    {
        using var client = Client();
        var (set, _) = await CreateSet(client, 3);
        var response = await client.GetAsync($"/api/v1/sets/{set.Id}/positions/2/alternatives");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Data<SetAlternativesResponse>(response)).Items);
    }

    [Fact]
    public async Task Candidates_are_read_from_the_current_owner_library_with_current_metadata()
    {
        using var client = Client();
        var (set, tracks) = await CreateSet(client, 4);
        var selected = set.Tracks.Select(t => t.TrackId).ToHashSet();
        var unused = tracks.Single(t => !selected.Contains(t.Id));
        using var other = Client();
        await CreateSet(other, 4); // This owner's eligible tracks must never appear in the first owner's response.
        var url = $"/api/v1/sets/{set.Id}/positions/2/alternatives";
        Assert.Equal(unused.Id, Assert.Single((await Data<SetAlternativesResponse>(await client.GetAsync(url))).Items).TrackId);
        await Data<TrackResponse>(await client.PutAsJsonAsync($"/api/v1/tracks/{unused.Id}", new UpdateTrackRequest
        {
            ExpectedRevision = unused.Revision, Artist = unused.Artist, Title = unused.Title,
            DurationSeconds = unused.DurationSeconds, Bpm = 180, Energy = 5, Genre = "House"
        }));
        Assert.Empty((await Data<SetAlternativesResponse>(await client.GetAsync(url))).Items);
    }
}
