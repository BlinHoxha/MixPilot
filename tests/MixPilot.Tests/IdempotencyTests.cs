using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MixPilot.Infrastructure;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Application.Services;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Tests;

public sealed class IdempotencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ValidTrack = "{\"artist\":\"Idempotency DJ\",\"title\":\"Cancellation\",\"durationSeconds\":300,\"authorizationConfirmed\":true}";

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, string body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<string> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>();

    [Fact]
    public async Task OpenApi_documents_header_on_exactly_five_operations()
    {
        using var client = factory.Client(Guid.NewGuid().ToString());
        var document = await client.GetFromJsonAsync<JsonObject>("/swagger/v1/swagger.json");
        var paths = document!["paths"]!.AsObject();
        var withHeader = paths.SelectMany(path => path.Value!.AsObject().Select(operation =>
            (Path: path.Key, Method: operation.Key, Parameters: operation.Value!["parameters"]?.AsArray())))
            .Where(operation => operation.Parameters?.Any(parameter =>
                parameter?["name"]?.GetValue<string>() == "Idempotency-Key" &&
                parameter["in"]?.GetValue<string>() == "header") == true)
            .Select(operation => $"{operation.Method.ToUpperInvariant()} {operation.Path}")
            .OrderBy(value => value).ToArray();
        Assert.Equal(new[]
        {
            "POST /api/v1/collections", "POST /api/v1/imports/csv",
            "POST /api/v1/set-generation-jobs", "POST /api/v1/sets/generate", "POST /api/v1/tracks"
        }, withHeader);
    }

    [Fact]
    public async Task Four_post_endpoints_replay_exact_response_without_second_write()
    {
        var subject = Guid.NewGuid().ToString();
        using var client = factory.Client(subject);
        const string track = "{\"artist\":\"Idempotency DJ\",\"title\":\"First\",\"durationSeconds\":300,\"bpm\":124,\"energy\":5,\"authorizationConfirmed\":true}";
        const string import = "{\"csv\":\"artist,title,durationSeconds,bpm,energy\\nIdempotency DJ,Second,300,125,5\",\"authorizationConfirmed\":true}";
        const string collection = "{\"name\":\"Retry collection\",\"trackIds\":[]}";
        const string set = "{\"name\":\"Retry set\",\"requirements\":{\"durationSeconds\":600}}";
        var requests = new[]
        {
            ("/api/v1/tracks", track, HttpStatusCode.Created),
            ("/api/v1/imports/csv", import, HttpStatusCode.OK),
            ("/api/v1/collections", collection, HttpStatusCode.Created),
            ("/api/v1/sets/generate", set, HttpStatusCode.Created)
        };
        foreach (var (path, body, status) in requests)
        {
            var key = $"retry_{Guid.NewGuid():N}";
            var first = await Post(client, path, body, key);
            var firstBody = await first.Content.ReadAsStringAsync();
            Assert.Equal(status, first.StatusCode);
            var second = await Post(client, path, body, key);
            Assert.Equal(first.StatusCode, second.StatusCode);
            Assert.Equal(firstBody, await second.Content.ReadAsStringAsync());
            Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
            Assert.Equal(first.Headers.Location, second.Headers.Location);
        }
        await using var db = factory.OwnerContext(subject);
        Assert.Equal(2, await db.Tracks.CountAsync());
        Assert.Single(await db.Collections.ToArrayAsync());
        Assert.Single(await db.Sets.ToArrayAsync());
        var records = await db.IdempotencyRecords.ToArrayAsync();
        Assert.Equal(4, records.Length);
        Assert.All(records, record =>
        {
            Assert.Equal("completed", record.State);
            Assert.DoesNotContain("X-Test-Subject", record.ResponseBody ?? "");
            Assert.DoesNotContain(subject, record.ResponseBody ?? "");
            Assert.DoesNotContain("Authorization", record.ResponseBody ?? "");
        });
    }

    [Fact]
    public async Task Reused_invalid_pending_expired_and_owner_scoped_keys_follow_contract()
    {
        var subject = Guid.NewGuid().ToString();
        using var owner = factory.Client(subject);
        using var other = factory.Client(Guid.NewGuid().ToString());
        const string body = "{\"artist\":\"Idempotency DJ\",\"title\":\"Scoped\",\"durationSeconds\":300,\"authorizationConfirmed\":true}";
        var key = $"scope_{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await Post(owner, "/api/v1/tracks", body, key)).StatusCode);
        var reused = await Post(owner, "/api/v1/tracks", body.Replace("Scoped", "Different"), key);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Equal("idempotency_key_reused", await Code(reused));
        var otherRoute = await Post(owner, "/api/v1/collections", "{\"name\":\"Different route\"}", key);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otherRoute.StatusCode);
        Assert.Equal("idempotency_key_reused", await Code(otherRoute));
        var invalid = await Post(owner, "/api/v1/tracks", body, "bad key");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_idempotency_key", await Code(invalid));
        Assert.Equal(HttpStatusCode.Created, (await Post(other, "/api/v1/tracks", body, key)).StatusCode);

        var pendingKey = $"pending_{Guid.NewGuid():N}";
        var pendingBody = body.Replace("Scoped", "PendingOnly");
        await using (var db = factory.OwnerContext(subject))
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = pendingKey, Method = "POST", Route = "api/v1/tracks",
                BodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pendingBody))),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }
        var pending = await Post(owner, "/api/v1/tracks", pendingBody, pendingKey);
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        Assert.Equal("idempotency_in_progress", await Code(pending));
        await using (var db = factory.OwnerContext(subject))
        {
            Assert.NotNull(await db.IdempotencyRecords.SingleOrDefaultAsync(r => r.Key == pendingKey));
            await using var outsider = factory.OwnerContext(Guid.NewGuid().ToString());
            Assert.Null(await outsider.IdempotencyRecords.SingleOrDefaultAsync(r => r.Key == pendingKey));
            var record = await db.IdempotencyRecords.SingleAsync(r => r.Key == pendingKey);
            record.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created, (await Post(owner, "/api/v1/tracks", pendingBody, pendingKey)).StatusCode);
    }

    [Fact]
    public async Task Four_hundred_replays_and_server_error_releases_reservation()
    {
        using var client = factory.Client(Guid.NewGuid().ToString());
        const string body = "{\"artist\":\"Idempotency DJ\",\"title\":\"Duplicate\",\"durationSeconds\":300,\"authorizationConfirmed\":true}";
        Assert.Equal(HttpStatusCode.Created, (await Post(client, "/api/v1/tracks", body, null)).StatusCode);
        var key = $"error_{Guid.NewGuid():N}";
        var first = await Post(client, "/api/v1/tracks", body, key);
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal("duplicate_track", await Code(first));
        var replay = await Post(client, "/api/v1/tracks", body, key);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());

        var gate = new FailureGate();
        using var failingFactory = new ApiFactory(services =>
        {
            services.AddScoped<TrackService>();
            services.RemoveAll<ITrackService>();
            services.AddScoped<ITrackService>(provider => new FailOnceTrackService(provider.GetRequiredService<TrackService>(), gate));
        });
        using var failingClient = failingFactory.Client(Guid.NewGuid().ToString());
        var failKey = $"fail_{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.InternalServerError, (await Post(failingClient, "/api/v1/tracks", body, failKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(failingClient, "/api/v1/tracks", body, failKey)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_cancellation_releases_before_commit_or_completes_after_commit(bool commitBeforeCancellation)
    {
        var gate = new CancellationGate(commitBeforeCancellation);
        using var testFactory = new ApiFactory(services =>
        {
            services.AddScoped<TrackService>();
            services.RemoveAll<ITrackService>();
            services.AddScoped<ITrackService>(provider => new CancellableTrackService(provider.GetRequiredService<TrackService>(), gate));
        });
        var subject = Guid.NewGuid().ToString();
        using var client = testFactory.Client(subject);
        var key = $"cancel_{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tracks")
        { Content = new StringContent(ValidTrack, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", key);
        var pending = client.SendAsync(request, cancellation.Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);

        IdempotencyRecord? record = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var db = testFactory.OwnerContext(subject);
            record = await db.IdempotencyRecords.SingleOrDefaultAsync(r => r.Key == key);
            if (commitBeforeCancellation ? record?.State == "completed" : record is null) break;
            await Task.Delay(20);
        }
        Assert.Equal(commitBeforeCancellation ? "completed" : null, record?.State);

        var retry = await Post(client, "/api/v1/tracks", ValidTrack, key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(commitBeforeCancellation, retry.Headers.Contains("Idempotent-Replayed"));
        await using var verify = testFactory.OwnerContext(subject);
        Assert.Equal(1, await verify.Tracks.CountAsync());
    }

    [Fact]
    public async Task Failed_save_mapped_to_conflict_is_stored_without_retrying_tracked_entity()
    {
        using var testFactory = new ApiFactory(services =>
        {
            services.RemoveAll<ITrackService>();
            services.AddScoped<ITrackService, FailingSaveTrackService>();
        });
        var subject = Guid.NewGuid().ToString();
        using var client = testFactory.Client(subject);
        var key = $"save_{Guid.NewGuid():N}";
        var first = await Post(client, "/api/v1/tracks", ValidTrack, key);
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal("resource_conflict", await Code(first));
        var replay = await Post(client, "/api/v1/tracks", ValidTrack, key);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        await using var db = testFactory.OwnerContext(subject);
        Assert.Empty(await db.Tracks.ToArrayAsync());
        var record = await db.IdempotencyRecords.SingleAsync(r => r.Key == key);
        Assert.Equal("completed", record.State);
        Assert.Equal(409, record.StatusCode);
    }

    private sealed class CancellationGate(bool commitBeforeCancellation)
    {
        public bool CommitBeforeCancellation { get; } = commitBeforeCancellation;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Remaining = 1;
    }

    private sealed class CancellableTrackService(ITrackService inner, CancellationGate gate) : ITrackService
    {
        public Task<PageResponse<TrackResponse>> Search(TrackQuery query, CancellationToken ct) => inner.Search(query, ct);
        public Task<TrackResponse> Get(Guid id, CancellationToken ct) => inner.Get(id, ct);
        public async Task<TrackResponse> Create(CreateTrackRequest request, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref gate.Remaining, 0) != 1) return await inner.Create(request, ct);
            TrackResponse? created = gate.CommitBeforeCancellation ? await inner.Create(request, ct) : null;
            gate.Entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) when (created is not null) { return created; }
            throw new OperationCanceledException(ct);
        }
        public Task<TrackResponse> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) => inner.Update(id, request, ct);
        public Task Delete(Guid id, int revision, CancellationToken ct) => inner.Delete(id, revision, ct);
        public Task<ImportResponse> Import(CsvImportRequest request, CancellationToken ct) => inner.Import(request, ct);
    }

    private sealed class FailingSaveTrackService(MixPilotDbContext db) : ITrackService
    {
        public Task<PageResponse<TrackResponse>> Search(TrackQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<TrackResponse> Get(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public async Task<TrackResponse> Create(CreateTrackRequest request, CancellationToken ct)
        {
            db.Tracks.Add(new Track { Id = Guid.NewGuid(), Artist = null!, Title = "Invalid", IdentityKey = "invalid", DurationSeconds = 300 });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw new RuleViolation("resource_conflict", "The track could not be saved.", 409); }
            throw new InvalidOperationException("The invalid track was unexpectedly saved.");
        }
        public Task<TrackResponse> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task Delete(Guid id, int revision, CancellationToken ct) => throw new NotSupportedException();
        public Task<ImportResponse> Import(CsvImportRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FailureGate { public int Remaining = 1; }

    private sealed class FailOnceTrackService(ITrackService inner, FailureGate gate) : ITrackService
    {
        public Task<PageResponse<TrackResponse>> Search(TrackQuery query, CancellationToken ct) => inner.Search(query, ct);
        public Task<TrackResponse> Get(Guid id, CancellationToken ct) => inner.Get(id, ct);
        public Task<TrackResponse> Create(CreateTrackRequest request, CancellationToken ct) =>
            Interlocked.Exchange(ref gate.Remaining, 0) == 1 ? throw new InvalidOperationException("Injected failure") : inner.Create(request, ct);
        public Task<TrackResponse> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) => inner.Update(id, request, ct);
        public Task Delete(Guid id, int revision, CancellationToken ct) => inner.Delete(id, revision, ct);
        public Task<ImportResponse> Import(CsvImportRequest request, CancellationToken ct) => inner.Import(request, ct);
    }
}
