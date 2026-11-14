using System.Data;
using System.Data.Common;
using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Infrastructure.Persistence.Repositories;

public sealed class SetGenerationJobRepository(MixPilotDbContext context) : ISetGenerationJobRepository
{
    public async Task Add(SetGenerationJob job, CancellationToken ct)
    {
        context.SetGenerationJobs.Add(job);
        await context.SaveChangesAsync(ct);
    }

    public Task<SetGenerationJob?> Get(Guid id, CancellationToken ct) =>
        context.SetGenerationJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct);

    public async Task<PageResponse<SetGenerationJob>> Search(PageQuery query, CancellationToken ct)
    {
        var jobs = context.SetGenerationJobs.AsNoTracking();
        var count = await jobs.CountAsync(ct);
        var items = await jobs.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return PageResponse<SetGenerationJob>.Create(items, query.PageNumber, query.PageSize, count);
    }

    public async Task<SetGenerationJob> Cancel(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var job = await Get(id, ct) ?? throw new RuleViolation("not_found", "The job was not found.", 404);
        int updated = job.Status switch
        {
            "queued" => await context.SetGenerationJobs.Where(j => j.Id == id && j.Status == "queued")
                .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.Status, "cancelled")
                    .SetProperty(j => j.UpdatedAt, now), ct),
            "running" => await context.SetGenerationJobs.Where(j => j.Id == id && j.Status == "running" && !j.CancelRequested)
                .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.CancelRequested, true)
                    .SetProperty(j => j.UpdatedAt, now), ct),
            _ => 0
        };
        if (updated != 1) throw new RuleViolation("job_not_cancellable", "The job cannot be cancelled.", 409);
        return (await Get(id, ct))!;
    }

    public async Task<bool> CancelIfRequested(Guid id, int attempt, DateTimeOffset now, CancellationToken ct) =>
        await context.SetGenerationJobs.Where(j => j.Id == id && j.Status == "running" &&
            j.Attempts == attempt && j.CancelRequested)
            .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.Status, "cancelled")
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.UpdatedAt, now), ct) == 1;

    public Task SetProgress(Guid id, int attempt, int progress, DateTimeOffset now, CancellationToken ct) =>
        context.SetGenerationJobs.Where(j => j.Id == id && j.Status == "running" && j.Attempts == attempt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.Progress, progress)
                .SetProperty(j => j.UpdatedAt, now), ct);

    public async Task Finish(Guid id, int attempt, string status, string? code, Guid? setId, DateTimeOffset now, CancellationToken ct)
    {
        var updated = await context.SetGenerationJobs.Where(j => j.Id == id && j.Status == "running" && j.Attempts == attempt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.Status, status)
                .SetProperty(j => j.Progress, 100)
                .SetProperty(j => j.ErrorCode, code)
                .SetProperty(j => j.ResultSetId, setId)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.UpdatedAt, now), ct);
        if (updated != 1) throw new InvalidOperationException("The job lease is no longer owned by this worker.");
    }

    public async Task<bool> RenewLease(Guid id, int attempt, DateTimeOffset now, CancellationToken ct) =>
        await context.SetGenerationJobs.Where(j => j.Id == id && j.Status == "running" && j.Attempts == attempt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.LeaseExpiresAt, now.AddMinutes(2)), ct) == 1;
}

// The claim is the only cross-owner operation. It reads an id and owner key under a database
// row lock; all job content and set access then runs in a fresh owner-scoped request context.
public sealed class JobClaimRepository(MixPilotDbContext context) : IJobClaimRepository
{
    public async Task<JobClaim?> ClaimNext(DateTimeOffset now, CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        bool close = connection.State == ConnectionState.Closed;
        if (close) await connection.OpenAsync(ct);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = """
                SELECT "Id", "OwnerKey", "Attempts", "CancelRequested"
                FROM "SetGenerationJobs"
                WHERE "Status" = 'queued' OR ("Status" = 'running' AND "LeaseExpiresAt" <= @now)
                ORDER BY "CreatedAt", "Id"
                LIMIT 1
                """ + (context.Database.IsNpgsql() ? " FOR UPDATE SKIP LOCKED" : "");
            Add(select, "@now", DatabaseTime(now));
            Guid id;
            string owner;
            int attempts;
            bool cancelRequested;
            await using (var reader = await select.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) return null;
                id = reader.GetGuid(0);
                owner = reader.GetString(1);
                attempts = reader.GetInt32(2);
                cancelRequested = reader.GetBoolean(3);
            }
            bool exhausted = attempts >= 3 || cancelRequested;
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = exhausted
                ? """
                    UPDATE "SetGenerationJobs" SET "Status" = @status, "ErrorCode" = @error,
                        "Progress" = 100, "LeaseExpiresAt" = NULL, "UpdatedAt" = @now
                    WHERE "Id" = @id
                    """
                : """
                    UPDATE "SetGenerationJobs" SET "Status" = 'running', "Attempts" = @attempt,
                        "Progress" = 10, "LeaseExpiresAt" = @lease, "UpdatedAt" = @now
                    WHERE "Id" = @id
                    """;
            Add(update, "@id", id);
            Add(update, "@now", DatabaseTime(now));
            if (exhausted)
            {
                Add(update, "@status", cancelRequested ? "cancelled" : "failed");
                Add(update, "@error", cancelRequested ? DBNull.Value : "job_attempts_exhausted");
            }
            else
            {
                Add(update, "@attempt", attempts + 1);
                Add(update, "@lease", DatabaseTime(now.AddMinutes(2)));
            }
            if (await update.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Job claim update failed.");
            await transaction.CommitAsync(ct);
            return new(id, owner, attempts + (exhausted ? 0 : 1), exhausted);
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    private object DatabaseTime(DateTimeOffset value) => context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
        ? new DateTimeOffsetToBinaryConverter().ConvertToProvider(value)!
        : value;

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
