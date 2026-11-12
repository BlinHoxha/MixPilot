using Microsoft.EntityFrameworkCore;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Domain;

namespace MixPilot.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(MixPilotDbContext context) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> Find(string key, CancellationToken ct) =>
        context.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Key == key, ct);

    public async Task Reserve(IdempotencyRecord record, CancellationToken ct)
    {
        context.IdempotencyRecords.Add(record);
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            context.Entry(record).State = EntityState.Detached;
            if (await Find(record.Key, ct) is not null)
                throw new RuleViolation("idempotency_in_progress", "A request with this key is already in progress.", 409);
            throw;
        }
    }

    public async Task Complete(Guid id, int statusCode, string body, string contentType, string? location, CancellationToken ct)
    {
        var updated = await context.IdempotencyRecords.Where(r => r.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.State, "completed")
            .SetProperty(r => r.StatusCode, statusCode)
            .SetProperty(r => r.ResponseBody, body)
            .SetProperty(r => r.ContentType, contentType)
            .SetProperty(r => r.Location, location), ct);
        if (updated != 1) throw new InvalidOperationException("Idempotency reservation was not found for this owner.");
    }

    public Task Release(Guid id, CancellationToken ct) =>
        context.IdempotencyRecords.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
}
