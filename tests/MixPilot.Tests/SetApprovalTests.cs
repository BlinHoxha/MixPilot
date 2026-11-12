using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Framework.Contracts.Common;
using MixPilot.Contracts;

namespace MixPilot.Tests;

public sealed class SetApprovalTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private HttpClient Client() => factory.Client(Guid.NewGuid().ToString());

    private static async Task<T> Data<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<CommonResult<T>>(Json))!.Data!;
    }

    private static async Task<string> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>();

    private static async Task<(SetResponse Set, TrackResponse[] Tracks)> CreateSet(HttpClient client)
    {
        var tracks = new List<TrackResponse>();
        for (var i = 0; i < 4; i++)
            tracks.Add(await Data<TrackResponse>(await client.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
            {
                Artist = "Approval test", Title = $"Track {i}", DurationSeconds = 300,
                Bpm = 124, Energy = 5, Genre = "House", AuthorizationConfirmed = true
            }, Json)));
        var set = await Data<SetResponse>(await client.PostAsJsonAsync("/api/v1/sets/generate", new GenerateSetRequest
        {
            Name = "Approval test", Requirements = new() { DurationSeconds = 900, Genre = "House" }
        }, Json));
        return (set, tracks.ToArray());
    }

    [Fact]
    public async Task Approval_is_revisioned_idempotent_and_version_statuses_follow_edits_and_regeneration()
    {
        using var client = Client();
        var (created, _) = await CreateSet(client);
        var root = $"/api/v1/sets/{created.Id}";
        Assert.Null(created.ApprovedVersion);
        Assert.Equal(SetVersionStatus.Draft, created.Status);

        var approvedResponse = await client.PostAsJsonAsync($"{root}/versions/1/approve", new ApproveSetVersionRequest { ExpectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        var approved = await Data<SetResponse>(approvedResponse);
        Assert.Equal(2, approved.Revision);
        Assert.Equal(1, approved.ApprovedVersion);
        Assert.Equal(SetVersionStatus.Approved, approved.Status);
        Assert.Equal(1, approved.Version);

        var repeated = await Data<SetResponse>(await client.PostAsJsonAsync($"{root}/versions/1/approve", new ApproveSetVersionRequest { ExpectedRevision = 2 }));
        Assert.Equal(2, repeated.Revision);
        Assert.Equal(1, repeated.ApprovedVersion);

        var edited = await Data<SetResponse>(await client.PutAsJsonAsync($"{root}/tracks", new EditSetRequest
        {
            ExpectedRevision = 2, OrderedTrackIds = created.Tracks.Select(t => t.TrackId).Reverse().ToArray()
        }));
        Assert.Equal(SetVersionStatus.Draft, edited.Status);
        Assert.Equal(2, edited.Version);
        Assert.Equal(1, edited.ApprovedVersion);
        var requirements = new SetRequirementsRequest { DurationSeconds = 900, Genre = "House" };
        var regenerated = await Data<SetResponse>(await client.PostAsJsonAsync($"{root}/regenerate", new RegenerateSetRequest
        {
            ExpectedRevision = edited.Revision, Requirements = requirements
        }, Json));
        Assert.Equal(SetVersionStatus.Draft, regenerated.Status);
        Assert.Equal(3, regenerated.Version);
        Assert.Equal(1, regenerated.ApprovedVersion);
        Assert.Equal(SetVersionStatus.Approved, (await Data<SetResponse>(await client.GetAsync($"{root}/versions/1"))).Status);

        var newer = await Data<SetResponse>(await client.PostAsJsonAsync($"{root}/versions/{regenerated.Version}/approve",
            new ApproveSetVersionRequest { ExpectedRevision = regenerated.Revision }));
        Assert.Equal(regenerated.Version, newer.ApprovedVersion);
        Assert.Equal(SetVersionStatus.Approved, newer.Status);
        Assert.Equal(SetVersionStatus.Superseded, (await Data<SetResponse>(await client.GetAsync($"{root}/versions/1"))).Status);
        var versions = await Data<SetVersionResponse[]>(await client.GetAsync($"{root}/versions"));
        Assert.Equal(3, versions.Length);
        Assert.Equal(SetVersionStatus.Superseded, versions.Single(v => v.Version == 1).Status);
        Assert.NotNull(versions.Single(v => v.Version == 1).ApprovedAt);
        Assert.Equal(SetVersionStatus.Approved, versions.Single(v => v.Version == newer.Version).Status);
        Assert.NotNull(versions.Single(v => v.Version == newer.Version).ApprovedAt);
        Assert.Equal(SetVersionStatus.Draft, versions.Single(v => v.Version == edited.Version).Status);
        Assert.Null(versions.Single(v => v.Version == edited.Version).ApprovedAt);

        var firstApprovalTime = versions.Single(v => v.Version == 1).ApprovedAt;
        var restored = await Data<SetResponse>(await client.PostAsJsonAsync($"{root}/versions/1/approve",
            new ApproveSetVersionRequest { ExpectedRevision = newer.Revision }));
        Assert.Equal(1, restored.ApprovedVersion);
        Assert.Equal(SetVersionStatus.Approved, restored.Status);
        var restoredVersions = await Data<SetVersionResponse[]>(await client.GetAsync($"{root}/versions"));
        Assert.Equal(firstApprovalTime, restoredVersions.Single(v => v.Version == 1).ApprovedAt);
        Assert.Equal(SetVersionStatus.Superseded, restoredVersions.Single(v => v.Version == newer.Version).Status);
    }

    [Fact]
    public async Task Approval_rejects_stale_invalid_and_unowned_requests()
    {
        using var owner = Client(); using var other = Client();
        var (set, _) = await CreateSet(owner);
        var root = $"/api/v1/sets/{set.Id}";
        var stale = await owner.PostAsJsonAsync($"{root}/versions/1/approve", new ApproveSetVersionRequest { ExpectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("revision_conflict", await Code(stale));
        var invalid = await owner.PostAsJsonAsync($"{root}/versions/1/approve", new { expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
        Assert.Equal("validation_failed", await Code(invalid));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"{root}/versions/99/approve", new ApproveSetVersionRequest { ExpectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/v1/sets/{Guid.NewGuid()}/versions/1/approve", new ApproveSetVersionRequest { ExpectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"{root}/versions/1/approve", new ApproveSetVersionRequest { ExpectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{root}/versions")).StatusCode);
        Assert.Null((await Data<SetResponse>(await owner.GetAsync(root))).ApprovedVersion);
    }

    [Fact]
    public async Task Approval_rechecks_current_track_eligibility_without_changing_set_on_failure()
    {
        using var client = Client();
        var (set, tracks) = await CreateSet(client);
        var selected = tracks.Single(t => t.Id == set.Tracks[0].TrackId);
        await Data<TrackResponse>(await client.PutAsJsonAsync($"/api/v1/tracks/{selected.Id}", new UpdateTrackRequest
        {
            Artist = selected.Artist, Title = selected.Title, DurationSeconds = selected.DurationSeconds,
            Bpm = 180, Energy = selected.Energy, Genre = selected.Genre, ExpectedRevision = selected.Revision
        }));
        var root = $"/api/v1/sets/{set.Id}";
        var failed = await client.PostAsJsonAsync($"{root}/versions/1/approve", new ApproveSetVersionRequest { ExpectedRevision = 1 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
        Assert.Equal("track_ineligible", await Code(failed));
        var unchanged = await Data<SetResponse>(await client.GetAsync(root));
        Assert.Equal(1, unchanged.Revision);
        Assert.Null(unchanged.ApprovedVersion);
        Assert.Equal(SetVersionStatus.Draft, unchanged.Status);
        Assert.Null((await Data<SetVersionResponse[]>(await client.GetAsync($"{root}/versions"))).Single().ApprovedAt);
    }
}
