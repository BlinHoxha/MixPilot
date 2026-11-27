using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Services;
using MixPilot.Contracts;
using MixPilot.Domain;
using MixPilot.Infrastructure;
using MixPilot.Infrastructure.Persistence;
using MixPilot.Infrastructure.Persistence.Repositories;

namespace MixPilot.Tests;

public sealed class PostgresIntegrationTests : IAsyncLifetime
{
    private const string Owner = "postgres-owner-a";
    private const string Other = "postgres-owner-b";
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture fixture = new();
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(25));
    private CancellationToken Ct => timeout.Token;

    public Task InitializeAsync() => fixture.InitializeAsync();
    public async Task DisposeAsync()
    {
        try { await fixture.DisposeAsync(); }
        finally { timeout.Dispose(); }
    }

    [PostgresFact]
    public async Task Empty_database_applies_all_migrations_and_matches_the_model()
    {
        await using var db = fixture.Create(Owner);
        var expected = db.Database.GetMigrations().ToArray();
        Assert.Equal(4, expected.Length);
        Assert.Equal(expected, (await db.Database.GetAppliedMigrationsAsync(Ct)).ToArray());
        Assert.False(db.Database.HasPendingModelChanges());
        await db.Database.OpenConnectionAsync(Ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";
        command.CommandTimeout = 10;
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var tables = new List<string>();
        while (await reader.ReadAsync(Ct)) tables.Add(reader.GetString(0));
        foreach (var table in new[] { "Tracks", "SetVersion", "IdempotencyRecords", "SetGenerationJobs" })
            Assert.Contains(table, tables);
    }

    [PostgresFact]
    public async Task Eight_workers_claim_every_queued_job_exactly_once()
    {
        var jobs = Enumerable.Range(0, 24).Select(i => Job(i)).ToArray();
        await using (var seed = fixture.Create(Owner))
        {
            seed.SetGenerationJobs.AddRange(jobs);
            await seed.SaveChangesAsync(Ct);
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = 8;
        var claimed = new ConcurrentBag<JobClaim>();
        var workers = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = fixture.Create("claim-worker");
            // Open all eight independent connections before releasing the start barrier.
            try { await db.Database.OpenConnectionAsync(Ct); }
            catch (Exception error) { allReady.TrySetException(error); throw; }
            if (Interlocked.Decrement(ref remaining) == 0) allReady.SetResult();
            await start.Task.WaitAsync(Ct);
            var repository = new JobClaimRepository(db);
            while (await repository.ClaimNext(Now, Ct) is { } claim) claimed.Add(claim);
        }).ToArray();
        try { await allReady.Task.WaitAsync(Ct); }
        finally { start.TrySetResult(); }
        await Task.WhenAll(workers);
        Assert.Equal(jobs.Length, claimed.Count);
        Assert.Equal(jobs.Select(j => j.Id).Order(), claimed.Select(j => j.Id).Order());
        Assert.Equal(jobs.Length, claimed.Select(j => j.Id).Distinct().Count());
        Assert.All(claimed, claim =>
        {
            Assert.Equal(Owner, claim.OwnerKey);
            Assert.Equal(1, claim.Attempt);
            Assert.False(claim.Exhausted);
        });
        await using var verify = fixture.Create(Owner);
        var rows = await verify.SetGenerationJobs.ToArrayAsync(Ct);
        Assert.Equal(jobs.Length, rows.Length);
        Assert.All(rows, job =>
        {
            Assert.Equal("running", job.Status);
            Assert.Equal(1, job.Attempts);
            Assert.Equal(Now.AddMinutes(2), job.LeaseExpiresAt);
        });
        Assert.Null(await new JobClaimRepository(verify).ClaimNext(Now, Ct));
    }

    [PostgresFact]
    public async Task Claim_skips_an_oldest_row_locked_by_another_transaction()
    {
        var oldest = Job(0);
        var next = Job(1);
        await using (var seed = fixture.Create(Owner))
        {
            seed.SetGenerationJobs.AddRange(oldest, next);
            await seed.SaveChangesAsync(Ct);
        }
        await using var locked = fixture.Create(Owner);
        await using var transaction = await locked.Database.BeginTransactionAsync(Ct);
        await locked.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SetGenerationJobs\" WHERE \"Id\" = {oldest.Id} FOR UPDATE", Ct);
        await using var worker = fixture.Create(Owner);
        // The row lock stays held until after ClaimNext completes. Without SKIP LOCKED this times out.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var claim = await new JobClaimRepository(worker).ClaimNext(Now, deadline.Token);
        Assert.NotNull(claim);
        Assert.Equal(next.Id, claim.Id);
        await transaction.RollbackAsync(Ct);
        Assert.Equal(oldest.Id, (await new JobClaimRepository(worker).ClaimNext(Now, Ct))!.Id);
    }

    [PostgresFact]
    public async Task Leases_renew_reclaim_exhaust_cancel_and_reject_stale_finishes()
    {
        var job = Job(0);
        await using var db = fixture.Create(Owner);
        db.SetGenerationJobs.Add(job);
        await db.SaveChangesAsync(Ct);
        var claims = new JobClaimRepository(db);
        var repository = new SetGenerationJobRepository(db);
        var first = (await claims.ClaimNext(Now, Ct))!;
        Assert.Equal(1, first.Attempt);
        Assert.True(await repository.RenewLease(job.Id, first.Attempt, Now.AddMinutes(1), Ct));
        Assert.Equal(Now.AddMinutes(3), (await repository.Get(job.Id, Ct))!.LeaseExpiresAt);
        Assert.False(await repository.RenewLease(job.Id, first.Attempt + 1, Now.AddMinutes(2), Ct));
        Assert.Equal(Now.AddMinutes(3), (await repository.Get(job.Id, Ct))!.LeaseExpiresAt);
        Assert.Null(await claims.ClaimNext(Now.AddMinutes(2), Ct));
        var second = (await claims.ClaimNext(Now.AddMinutes(4), Ct))!;
        Assert.Equal(2, second.Attempt);
        Assert.False(second.Exhausted);
        var before = (await repository.Get(job.Id, Ct))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Finish(
            job.Id, first.Attempt, "succeeded", null, Guid.NewGuid(), Now.AddMinutes(4), Ct));
        var after = (await repository.Get(job.Id, Ct))!;
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Progress, after.Progress);
        Assert.Equal(before.LeaseExpiresAt, after.LeaseExpiresAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.ResultSetId, after.ResultSetId);
        Assert.Equal(before.ErrorCode, after.ErrorCode);
        var third = (await claims.ClaimNext(Now.AddMinutes(7), Ct))!;
        Assert.Equal(3, third.Attempt);
        Assert.False(third.Exhausted);
        var exhausted = (await claims.ClaimNext(Now.AddMinutes(10), Ct))!;
        Assert.True(exhausted.Exhausted);
        Assert.Equal(3, exhausted.Attempt);
        var failed = (await repository.Get(job.Id, Ct))!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("job_attempts_exhausted", failed.ErrorCode);
        Assert.Null(failed.LeaseExpiresAt);

        var cancelled = Job(1);
        db.SetGenerationJobs.Add(cancelled);
        await db.SaveChangesAsync(Ct);
        var cancelledClaim = (await claims.ClaimNext(Now, Ct))!;
        await repository.Cancel(cancelled.Id, Now.AddMinutes(1), Ct);
        var cancelledExpired = (await claims.ClaimNext(Now.AddMinutes(3), Ct))!;
        Assert.Equal(cancelledClaim.Id, cancelledExpired.Id);
        Assert.True(cancelledExpired.Exhausted);
        var cancelledRow = (await repository.Get(cancelled.Id, Ct))!;
        Assert.Equal("cancelled", cancelledRow.Status);
        Assert.Null(cancelledRow.LeaseExpiresAt);
        Assert.Null(cancelledRow.ErrorCode);
    }

    [PostgresFact]
    public async Task Lost_attempt_rolls_back_generated_set_and_version_then_current_attempt_commits_both()
    {
        var job = Job(0);
        await using var db = fixture.Create(Owner);
        db.Tracks.Add(Track());
        db.SetGenerationJobs.Add(job);
        await db.SaveChangesAsync(Ct);
        var first = (await new JobClaimRepository(db).ClaimNext(Now, Ct))!;
        // Another connection reclaims the job before the first worker tries to finish.
        await using var reclaimer = fixture.Create(Owner);
        var current = (await new JobClaimRepository(reclaimer).ClaimNext(Now.AddMinutes(3), Ct))!;
        var jobs = new SetGenerationJobRepository(db);
        var sets = new SetService(new SetRepository(db, NullLogger<SetRepository>.Instance),
            new TrackRepository(db, NullLogger<TrackRepository>.Instance), new DjEngine(),
            NullLogger<SetService>.Instance, TimeProvider.System);
        IUnitOfWork unit = new UnitOfWork(db);
        var request = new GenerateSetRequest { Name = "Transactional", Requirements = new() { DurationSeconds = 300 } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteInTransaction(async ct =>
        {
            var set = await sets.Generate(request, ct);
            // Prove real inserts occurred before Finish rejects the stale attempt.
            Assert.Equal(1, await db.Sets.CountAsync(ct));
            Assert.Equal(1, await db.Set<SetVersion>().CountAsync(ct));
            await jobs.Finish(job.Id, first.Attempt, "succeeded", null, set.Id, Now.AddMinutes(3), ct);
        }, Ct));
        await using (var verify = fixture.Create(Owner))
        {
            Assert.Empty(await verify.Sets.ToArrayAsync(Ct));
            Assert.Empty(await verify.Set<SetVersion>().ToArrayAsync(Ct));
            var running = await verify.SetGenerationJobs.SingleAsync(Ct);
            Assert.Equal("running", running.Status);
            Assert.Equal(current.Attempt, running.Attempts);
            Assert.Null(running.ResultSetId);
        }
        await unit.ExecuteInTransaction(async ct =>
        {
            var set = await sets.Generate(request, ct);
            await jobs.Finish(job.Id, current.Attempt, "succeeded", null, set.Id, Now.AddMinutes(3), ct);
        }, Ct);
        await using var committed = fixture.Create(Owner);
        var saved = await committed.Sets.SingleAsync(Ct);
        Assert.Single(await committed.Set<SetVersion>().ToArrayAsync(Ct));
        var finished = await committed.SetGenerationJobs.SingleAsync(Ct);
        Assert.Equal("succeeded", finished.Status);
        Assert.Equal(saved.Id, finished.ResultSetId);
        Assert.Null(finished.LeaseExpiresAt);
    }

    [PostgresFact]
    public async Task Concurrent_track_save_reports_revision_conflict()
    {
        await using var first = fixture.Create(Owner);
        var track = Track();
        first.Tracks.Add(track);
        await first.SaveChangesAsync(Ct);
        await using var second = fixture.Create(Owner);
        var competing = await second.Tracks.SingleAsync(Ct);
        track.Title = "First edit";
        track.Revision++;
        await first.SaveChangesAsync(Ct);
        competing.Title = "Second edit";
        competing.Revision++;
        var conflict = await Assert.ThrowsAsync<RuleViolation>(() =>
            new TrackRepository(second, NullLogger<TrackRepository>.Instance).SaveChanges(Ct));
        Assert.Equal("revision_conflict", conflict.Code);
        Assert.Equal(409, conflict.StatusCode);
        await using var verify = fixture.Create(Owner);
        Assert.Equal("First edit", (await verify.Tracks.SingleAsync(Ct)).Title);
    }

    [PostgresFact]
    public async Task Concurrent_idempotency_reservation_replay_and_release_are_owner_scoped()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = 2;
        var reservations = new[] { Record("same-key"), Record("same-key") };
        var tasks = reservations.Select(async record =>
        {
            await using var db = fixture.Create(Owner);
            try { await db.Database.OpenConnectionAsync(Ct); }
            catch (Exception error) { allReady.TrySetException(error); throw; }
            if (Interlocked.Decrement(ref remaining) == 0) allReady.SetResult();
            await start.Task.WaitAsync(Ct);
            try { await new IdempotencyStore(db).Reserve(record, Ct); return (RuleViolation?)null; }
            catch (RuleViolation conflict) { return conflict; }
        }).ToArray();
        try { await allReady.Task.WaitAsync(Ct); }
        finally { start.TrySetResult(); }
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, result => result is null);
        var conflict = Assert.Single(results.OfType<RuleViolation>());
        Assert.Equal("idempotency_in_progress", conflict.Code);
        Assert.Equal(409, conflict.StatusCode);
        await using var owner = fixture.Create(Owner);
        await using var other = fixture.Create(Other);
        var store = new IdempotencyStore(owner);
        var otherStore = new IdempotencyStore(other);
        var winner = (await store.Find("same-key", Ct))!;
        var otherRecord = Record("same-key");
        await otherStore.Reserve(otherRecord, Ct);
        Assert.Equal(Other, (await otherStore.Find("same-key", Ct))!.OwnerKey);
        const string body = "{\"id\":\"stored-response\"}";
        await store.Complete(winner.Id, 201, body, "application/json", "/api/v1/tracks/stored-response", Ct);
        var replay = (await store.Find("same-key", Ct))!;
        Assert.Equal("completed", replay.State);
        Assert.Equal(201, replay.StatusCode);
        Assert.Equal(body, replay.ResponseBody);
        Assert.Equal("application/json", replay.ContentType);
        Assert.Equal("/api/v1/tracks/stored-response", replay.Location);
        Assert.Equal("pending", (await otherStore.Find("same-key", Ct))!.State);
        await otherStore.Release(winner.Id, Ct);
        Assert.NotNull(await store.Find("same-key", Ct));
        await store.Release(winner.Id, Ct);
        Assert.Null(await store.Find("same-key", Ct));
        Assert.NotNull(await otherStore.Find("same-key", Ct));
        await otherStore.Release(otherRecord.Id, Ct);
        Assert.Null(await otherStore.Find("same-key", Ct));
    }

    [PostgresFact]
    public async Task Owner_filters_block_reads_bulk_updates_and_deletes_for_all_three_roots()
    {
        var track = Track();
        var job = Job(0);
        var record = Record("private-key");
        await using (var seed = fixture.Create(Owner))
        {
            seed.Tracks.Add(track);
            seed.SetGenerationJobs.Add(job);
            seed.IdempotencyRecords.Add(record);
            await seed.SaveChangesAsync(Ct);
        }
        await using var other = fixture.Create(Other);
        Assert.Empty(await other.Tracks.ToArrayAsync(Ct));
        Assert.Empty(await other.SetGenerationJobs.ToArrayAsync(Ct));
        Assert.Empty(await other.IdempotencyRecords.ToArrayAsync(Ct));
        Assert.Null(await new TrackRepository(other, NullLogger<TrackRepository>.Instance).GetById(track.Id, ct: Ct));
        Assert.Null(await new SetGenerationJobRepository(other).Get(job.Id, Ct));
        Assert.Null(await new IdempotencyStore(other).Find(record.Key, Ct));
        Assert.Equal(0, await other.Tracks.Where(t => t.Id == track.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, "Stolen"), Ct));
        Assert.Equal(0, await other.SetGenerationJobs.Where(j => j.Id == job.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "failed"), Ct));
        Assert.Equal(0, await other.IdempotencyRecords.Where(r => r.Id == record.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "completed"), Ct));
        Assert.Equal(0, await other.Tracks.Where(t => t.Id == track.Id).ExecuteDeleteAsync(Ct));
        Assert.Equal(0, await other.SetGenerationJobs.Where(j => j.Id == job.Id).ExecuteDeleteAsync(Ct));
        Assert.Equal(0, await other.IdempotencyRecords.Where(r => r.Id == record.Id).ExecuteDeleteAsync(Ct));
        await using var verify = fixture.Create(Owner);
        Assert.Equal(track.Title, (await verify.Tracks.SingleAsync(Ct)).Title);
        Assert.Equal("queued", (await verify.SetGenerationJobs.SingleAsync(Ct)).Status);
        Assert.Equal("pending", (await verify.IdempotencyRecords.SingleAsync(Ct)).State);
    }

    private static SetGenerationJob Job(int index) => new()
    {
        Name = $"PostgreSQL job {index}", CreatedAt = Now.AddSeconds(index), UpdatedAt = Now,
        RequirementsJson = "{\"durationSeconds\":300}", CorrelationId = $"pg-{index}"
    };

    private static Track Track() => new()
    {
        Id = Guid.NewGuid(), Artist = "PostgreSQL DJ", Title = "Transactional track", IdentityKey = "pg-track",
        DurationSeconds = 300, Bpm = 124, Energy = 5, Rights = new() { CanReference = true }
    };

    private static IdempotencyRecord Record(string key) => new()
    {
        Key = key, Method = "POST", Route = "/api/v1/tracks", BodyHash = new string('A', 64),
        CreatedAt = Now, ExpiresAt = Now.AddDays(1)
    };
}
