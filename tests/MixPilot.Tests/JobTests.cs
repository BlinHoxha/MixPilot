using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using Framework.Contracts.Common;
using Framework.Contracts.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MixPilot.Api;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;

namespace MixPilot.Tests;

public sealed class JobTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static CreateSetGenerationJobRequest Structured(string name) => new()
    { Name = name, Requirements = new() { DurationSeconds = 300 } };

    private static async Task<SetGenerationJobResponse> Submit(HttpClient client, CreateSetGenerationJobRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/set-generation-jobs", request, Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<CommonResult<SetGenerationJobResponse>>(Json))!.Data!;
        Assert.Equal($"/api/v1/jobs/{job.Id}", response.Headers.Location?.ToString());
        return job;
    }

    private static async Task SeedTrack(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
        { Artist = "Job test", Title = title, DurationSeconds = 300, Bpm = 124, Energy = 5, AuthorizationConfirmed = true });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JobClaim> Claim(ApiFactory factory, DateTimeOffset? now = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IJobClaimRepository>()
            .ClaimNext(now ?? DateTimeOffset.UtcNow, CancellationToken.None))!;
    }

    private static async Task Process(ApiFactory factory, JobClaim claim)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<JobOwnerContext>().SetForWorker(claim.OwnerKey);
        await scope.ServiceProvider.GetRequiredService<ISetGenerationJobProcessor>()
            .Process(claim.Id, claim.Attempt, CancellationToken.None);
    }

    [Fact]
    public async Task Failed_finish_rolls_back_set_and_reclaimed_attempt_creates_exactly_one()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid().ToString();
        using var client = factory.Client(owner);
        await SeedTrack(client, "Transactional track");
        var job = await Submit(client, Structured("Transactional set"));
        var first = await Claim(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<JobOwnerContext>().SetForWorker(first.OwnerKey);
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var sets = scope.ServiceProvider.GetRequiredService<ISetService>();
            var jobs = scope.ServiceProvider.GetRequiredService<ISetGenerationJobRepository>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteInTransaction(async ct =>
            {
                var created = await sets.Generate(new() { Name = "Rolled back", Requirements = Structured("ignored").Requirements! }, ct);
                await jobs.Finish(job.Id, first.Attempt + 1, "succeeded", null, created.Id, DateTimeOffset.UtcNow, ct);
            }, CancellationToken.None));
        }
        await using (var db = factory.OwnerContext(owner))
            Assert.Empty(await db.Sets.ToListAsync());
        var reclaimed = await Claim(factory, DateTimeOffset.UtcNow.AddMinutes(3));
        Assert.Equal(first.Attempt + 1, reclaimed.Attempt);
        await Process(factory, reclaimed);
        await using (var db = factory.OwnerContext(owner))
            Assert.Single(await db.Sets.ToListAsync());
    }

    [Fact]
    public async Task Generation_rule_failure_preserves_error_code_without_a_set()
    {
        using var factory = new ApiFactory();
        var owner = Guid.NewGuid().ToString();
        using var client = factory.Client(owner);
        var job = await Submit(client, Structured("No candidates"));
        await Process(factory, await Claim(factory));
        var failed = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{job.Id}", Json))!.Data!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("no_eligible_tracks", failed.ErrorCode);
        await using var db = factory.OwnerContext(owner);
        Assert.Empty(await db.Sets.ToListAsync());
    }

    [Fact]
    public void Worker_owner_assignment_rejects_HTTP_and_second_assignment()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var context = new JobOwnerContext(accessor);
        var key = new string('A', 64);
        Assert.Throws<InvalidOperationException>(() => context.SetForWorker(key));
        accessor.HttpContext = null;
        context.SetForWorker(key);
        Assert.Equal(key, context.OwnerKey);
        Assert.Throws<InvalidOperationException>(() => context.SetForWorker(new string('B', 64)));
        Assert.Null(new JobOwnerContext(accessor).OwnerKey);
    }

    [Fact]
    public async Task Submission_requires_exactly_one_input_form()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        var neither = await client.PostAsJsonAsync("/api/v1/set-generation-jobs", new CreateSetGenerationJobRequest { Name = "Empty" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        var both = await client.PostAsJsonAsync("/api/v1/set-generation-jobs", new CreateSetGenerationJobRequest
        { Name = "Both", Requirements = new(), Prompt = "House", Territory = "HU" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        var badTerritory = await client.PostAsJsonAsync("/api/v1/set-generation-jobs", new CreateSetGenerationJobRequest
        { Name = "Prompt", Prompt = "House", Territory = "hu" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, badTerritory.StatusCode);
    }

    [Fact]
    public async Task Idempotent_submission_and_two_owner_processing_stay_isolated()
    {
        using var factory = new ApiFactory();
        using var alice = factory.Client(Guid.NewGuid().ToString());
        using var bob = factory.Client(Guid.NewGuid().ToString());
        await SeedTrack(alice, "Alice job track");
        await SeedTrack(bob, "Bob job track");
        var request = Structured("Alice set");
        using var firstMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/set-generation-jobs")
        { Content = JsonContent.Create(request, options: Json) };
        firstMessage.Headers.Add("Idempotency-Key", "job_test_1");
        var first = await alice.SendAsync(firstMessage);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var aliceJob = (await first.Content.ReadFromJsonAsync<CommonResult<SetGenerationJobResponse>>(Json))!.Data!;
        using var retryMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/set-generation-jobs")
        { Content = JsonContent.Create(request, options: Json) };
        retryMessage.Headers.Add("Idempotency-Key", "job_test_1");
        var retry = await alice.SendAsync(retryMessage);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(aliceJob.Id, (await retry.Content.ReadFromJsonAsync<CommonResult<SetGenerationJobResponse>>(Json))!.Data!.Id);
        var bobJob = await Submit(bob, Structured("Bob set"));
        Assert.Equal("queued", aliceJob.Status);
        Assert.Equal("setGeneration", aliceJob.Kind);
        Assert.Equal(0, aliceJob.Progress);
        Assert.False(string.IsNullOrWhiteSpace(aliceJob.CorrelationId));

        var aliceList = (await alice.GetFromJsonAsync<CommonResult<PageResponse<SetGenerationJobResponse>>>("/api/v1/jobs", Json))!.Data!;
        Assert.Single(aliceList.Items);
        Assert.Equal(aliceJob.Id, aliceList.Items.Single().Id);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/jobs/{aliceJob.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/v1/jobs/{aliceJob.Id}/cancel", null)).StatusCode);

        var firstClaim = await Claim(factory);
        Assert.Equal(aliceJob.Id, firstClaim.Id);
        await Process(factory, firstClaim);
        var secondClaim = await Claim(factory);
        Assert.Equal(bobJob.Id, secondClaim.Id);
        await Process(factory, secondClaim);
        var completedAlice = (await alice.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{aliceJob.Id}", Json))!.Data!;
        var completedBob = (await bob.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{bobJob.Id}", Json))!.Data!;
        Assert.Equal("succeeded", completedAlice.Status);
        Assert.Equal("succeeded", completedBob.Status);
        Assert.Equal(100, completedAlice.Progress);
        Assert.NotEqual(completedAlice.ResultSetId, completedBob.ResultSetId);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/v1/sets/{completedAlice.ResultSetId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/sets/{completedAlice.ResultSetId}")).StatusCode);
    }

    [Fact]
    public async Task Cancellation_and_expired_leases_follow_state_machine()
    {
        var clock = new AdjustableClock();
        using var factory = new ApiFactory(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        using var client = factory.Client(Guid.NewGuid().ToString());
        var queued = await Submit(client, Structured("Queued"));
        var cancelled = (await (await client.PostAsync($"/api/v1/jobs/{queued.Id}/cancel", null))
            .Content.ReadFromJsonAsync<CommonResult<SetGenerationJobResponse>>(Json))!.Data!;
        Assert.Equal("cancelled", cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/jobs/{queued.Id}/cancel", null)).StatusCode);

        var running = await Submit(client, Structured("Running"));
        var claim = await Claim(factory, clock.GetUtcNow());
        Assert.Equal(running.Id, claim.Id);
        var requested = (await (await client.PostAsync($"/api/v1/jobs/{running.Id}/cancel", null))
            .Content.ReadFromJsonAsync<CommonResult<SetGenerationJobResponse>>(Json))!.Data!;
        Assert.Equal("running", requested.Status);
        await Process(factory, claim);
        var result = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{running.Id}", Json))!.Data!;
        Assert.Equal("cancelled", result.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/jobs/{running.Id}/cancel", null)).StatusCode);

        var abandoned = await Submit(client, Structured("Abandoned"));
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var reclaimed = await Claim(factory, clock.GetUtcNow());
            Assert.Equal(abandoned.Id, reclaimed.Id);
            Assert.Equal(attempt, reclaimed.Attempt);
            clock.Advance(TimeSpan.FromMinutes(3));
        }
        var exhausted = await Claim(factory, clock.GetUtcNow());
        Assert.True(exhausted.Exhausted);
        var failed = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{abandoned.Id}", Json))!.Data!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("job_attempts_exhausted", failed.ErrorCode);
    }

    [Fact]
    public async Task Prompt_failures_become_job_error_codes()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        var job = await Submit(client, new() { Name = "Prompt", Prompt = "House for an hour", Territory = "HU" });
        await Process(factory, await Claim(factory));
        var failed = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{job.Id}", Json))!.Data!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("prompt_interpretation_unavailable", failed.ErrorCode);
    }

    [Fact]
    public async Task Prompt_job_interprets_then_generates_and_honors_cancellation_between_steps()
    {
        var model = new BlockingModel();
        using var factory = new ApiFactory(services =>
        {
            services.RemoveAll<IStructuredGenerationModel>();
            services.AddSingleton<IStructuredGenerationModel>(model);
        });
        using var client = factory.Client(Guid.NewGuid().ToString());
        await SeedTrack(client, "Prompt job track");
        var request = new CreateSetGenerationJobRequest { Name = "Prompt set", Prompt = "Five minute set", Territory = "HU" };

        var cancelledJob = await Submit(client, request);
        var cancelledClaim = await Claim(factory);
        var processing = Process(factory, cancelledClaim);
        await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/jobs/{cancelledJob.Id}/cancel", null)).StatusCode);
        model.Release.SetResult();
        await processing;
        var cancelled = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{cancelledJob.Id}", Json))!.Data!;
        Assert.Equal("cancelled", cancelled.Status);

        var successfulJob = await Submit(client, request);
        await Process(factory, await Claim(factory));
        var succeeded = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{successfulJob.Id}", Json))!.Data!;
        Assert.Equal("succeeded", succeeded.Status);
        Assert.NotNull(succeeded.ResultSetId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/sets/{succeeded.ResultSetId}")).StatusCode);

        model.Status = StructuredGenerationStatus.Refused;
        var refusedJob = await Submit(client, request);
        await Process(factory, await Claim(factory));
        var refused = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{refusedJob.Id}", Json))!.Data!;
        Assert.Equal("failed", refused.Status);
        Assert.Equal("prompt_not_interpretable", refused.ErrorCode);
    }

    [Fact]
    public async Task Hosted_worker_processes_queued_job_on_sqlite()
    {
        using var factory = new ApiFactory();
        using var bootstrap = factory.Client("bootstrap");
        using var enabled = factory.WithWebHostBuilder(builder => builder.UseSetting("Jobs:Enabled", "true")
            .UseSetting("Jobs:PollSeconds", "0.1"));
        using var client = enabled.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        await SeedTrack(client, "Hosted job track");
        var job = await Submit(client, Structured("Hosted"));
        SetGenerationJobResponse? observed = null;
        for (var i = 0; i < 100; i++)
        {
            observed = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{job.Id}", Json))!.Data!;
            if (observed.Status is "succeeded" or "failed") break;
            await Task.Delay(50);
        }
        Assert.Equal("succeeded", observed?.Status);
        Assert.NotNull(observed?.ResultSetId);
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan amount) => current += amount;
    }

    private sealed class BlockingModel : IStructuredGenerationModel
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StructuredGenerationStatus Status { get; set; } = StructuredGenerationStatus.Completed;
        private int calls;
        public async Task<StructuredGenerationResult> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Entered.SetResult();
                await Release.Task.WaitAsync(ct);
            }
            const string output = """
                {"durationMinutes":5,"minBpm":110,"maxBpm":140,"genre":null,"mood":null,"hasVocals":null,"energyCurve":"wave","assumptions":[]}
                """;
            return new(Status, Status == StructuredGenerationStatus.Completed ? output : null, "fake", new(null, null));
        }
    }
}
