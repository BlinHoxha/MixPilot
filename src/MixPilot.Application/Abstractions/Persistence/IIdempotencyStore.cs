using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> Find(string key, CancellationToken ct);
    Task Reserve(IdempotencyRecord record, CancellationToken ct);
    Task Complete(Guid id, int statusCode, string body, string contentType, string? location, CancellationToken ct);
    Task Release(Guid id, CancellationToken ct);
}
