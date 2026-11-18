using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

using Npgsql;

namespace MixPilot.Infrastructure.Persistence.Repositories;

/// <summary>Owner-scoped EF operations and MixPilot's persistence error contract.</summary>
public abstract class MixPilotRepository<TEntity>(MixPilotDbContext context, ILogger logger)
    where TEntity : OwnedEntity
{
    protected ILogger Logger { get; } = logger;
    protected DbSet<TEntity> DbSet { get; } = context.Set<TEntity>();

    public virtual IQueryable<TEntity> Query(bool track = false) => track ? DbSet : DbSet.AsNoTracking();

    public virtual Task<TEntity?> GetById(Guid id, bool track = false, CancellationToken ct = default) =>
        Query(track).FirstOrDefaultAsync(entity => entity.Id == id, ct);

    public virtual Task Add(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return DbSet.AddAsync(entity, ct).AsTask();
    }

    public virtual Task AddRange(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return DbSet.AddRangeAsync(entities, ct);
    }

    public virtual Task Update(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public virtual async Task Remove(Guid id, CancellationToken ct = default)
    {
        TEntity entity = await GetById(id, true, ct)
            ?? throw new KeyNotFoundException($"{typeof(TEntity).Name} with id '{id}' was not found.");
        DbSet.Remove(entity);
    }

    public virtual async Task<int> SaveChanges(CancellationToken ct = default)
    {
        try { return await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new RuleViolation("revision_conflict", "The resource changed. Fetch the latest version before retrying.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        { throw new RuleViolation("resource_conflict", "A conflicting resource was created. Refresh and retry.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23503" })
        { throw new RuleViolation("resource_in_use", "Remove this track from its collections before deleting it.", 409); }
    }

    protected static async Task<PageResponse<T>> Page<T>(IQueryable<T> query, PageQuery page, CancellationToken ct) where T : OwnedEntity
    {
        int count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize).ToArrayAsync(ct);
        return PageResponse<T>.Create(items, page.PageNumber, page.PageSize, count);
    }
}

