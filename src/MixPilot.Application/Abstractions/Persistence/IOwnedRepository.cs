using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface IOwnedRepository<TEntity> where TEntity : OwnedEntity
{
    IQueryable<TEntity> Query(bool track = false);
    Task<TEntity?> GetById(Guid id, bool track = false, CancellationToken ct = default);
    Task Add(TEntity entity, CancellationToken ct = default);
    Task AddRange(IEnumerable<TEntity> entities, CancellationToken ct = default);
    Task Update(TEntity entity, CancellationToken ct = default);
    Task Remove(Guid id, CancellationToken ct = default);
    Task<int> SaveChanges(CancellationToken ct = default);
}
