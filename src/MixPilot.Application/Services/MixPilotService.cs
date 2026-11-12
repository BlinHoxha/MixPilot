using AutoMapper;
using Framework.Application.Abstractions.Persistence;
using Framework.Application.Services;
using Framework.Contracts.Abstractions;
using Microsoft.Extensions.Logging;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

/// <summary>Reuses Framework persistence orchestration after the typed use case validates its rules.</summary>
public abstract class MixPilotService<TEntity, TRepository>(TRepository repository, ILogger logger, IMapper mapper, TimeProvider clock)
    : BaseService<TEntity, Guid>(repository, logger, mapper)
    where TEntity : OwnedEntity
    where TRepository : IBaseRepository<TEntity, Guid>
{
    protected TRepository EntityRepository { get; } = repository;
    protected TimeProvider Clock { get; } = clock;

    // Entity-to-self maps preserve the validated aggregate. Framework performs Add/AddRange + one SaveChanges.
    protected Task<Guid> PersistNew(TEntity entity, CancellationToken ct) => base.Add(entity, ct);
    protected Task PersistNewRange(IEnumerable<TEntity> entities, CancellationToken ct) => base.AddRange(entities, ct);
    protected Task PersistChanges(CancellationToken ct) => Repository.SaveChanges(ct);
    protected Task RemoveValidated(Guid id, CancellationToken ct) => base.Remove(id, ct);
    protected void Touch(OwnedEntity entity) { entity.Revision++; entity.UpdatedAt = Clock.GetUtcNow(); }
    protected static RuleViolation Missing() => new("not_found", "The requested resource was not found.", 404);
    protected static void CheckRevision(OwnedEntity entity, int expected)
    {
        if (entity.Revision != expected)
            throw new RuleViolation("revision_conflict", "The resource changed. Fetch the latest version before retrying.", 409);
    }

    // Generic Framework writes do not carry MixPilot's authorization/revision requirements.
    // Keep them closed even if somebody casts this service to IBaseService; controllers use focused interfaces.
    private static RuleViolation ExplicitOperationRequired() => new("explicit_operation_required",
        "Use the typed MixPilot operation with its authorization and revision inputs.", 400);
    public sealed override Task<Guid> Add<TMap>(TMap dto, CancellationToken cancellationToken = default) => throw ExplicitOperationRequired();
    public sealed override Task AddRange<TMap>(IEnumerable<TMap> dtos, CancellationToken cancellationToken = default) => throw ExplicitOperationRequired();
    public sealed override Task<Guid> Update<TMap>(TMap dto, CancellationToken cancellationToken = default) => throw ExplicitOperationRequired();
    public sealed override Task UpdateRange<TMap>(IEnumerable<TMap> dtos, CancellationToken cancellationToken = default) => throw ExplicitOperationRequired();
    public sealed override Task Remove(Guid id, CancellationToken cancellationToken = default) => throw ExplicitOperationRequired();
    public sealed override Task RemoveRange(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) => throw ExplicitOperationRequired();
}

