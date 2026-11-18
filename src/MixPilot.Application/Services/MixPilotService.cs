using Framework.Contracts.Abstractions;
using MixPilot.Application.Abstractions.Persistence;
using Microsoft.Extensions.Logging;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

/// <summary>Persists validated aggregates through the owner-scoped repository.</summary>
public abstract class MixPilotService<TEntity, TRepository>(TRepository repository, ILogger logger, TimeProvider clock)
    where TEntity : OwnedEntity
    where TRepository : IOwnedRepository<TEntity>
{
    protected TRepository EntityRepository { get; } = repository;
    protected TimeProvider Clock { get; } = clock;

    protected async Task<Guid> PersistNew(TEntity entity, CancellationToken ct)
    {
        await EntityRepository.Add(entity, ct);
        await EntityRepository.SaveChanges(ct);
        logger.LogTrace("Added {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
        return entity.Id;
    }
    protected async Task PersistNewRange(IEnumerable<TEntity> entities, CancellationToken ct)
    {
        await EntityRepository.AddRange(entities, ct);
        await EntityRepository.SaveChanges(ct);
    }
    protected Task PersistChanges(CancellationToken ct) => EntityRepository.SaveChanges(ct);
    protected async Task RemoveValidated(Guid id, CancellationToken ct)
    {
        await EntityRepository.Remove(id, ct);
        await EntityRepository.SaveChanges(ct);
        logger.LogTrace("Removed {EntityType} with id {EntityId}", typeof(TEntity).Name, id);
    }
    protected void Touch(OwnedEntity entity) { entity.Revision++; entity.UpdatedAt = Clock.GetUtcNow(); }
    protected static RuleViolation Missing() => new("not_found", "The requested resource was not found.", 404);
    protected static void CheckRevision(OwnedEntity entity, int expected)
    {
        if (entity.Revision != expected)
            throw new RuleViolation("revision_conflict", "The resource changed. Fetch the latest version before retrying.", 409);
    }
}

