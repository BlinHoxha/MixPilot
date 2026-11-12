using AutoMapper;
using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

using Framework.Infrastructure.Persistence.Repositories;
using Npgsql;

namespace MixPilot.Infrastructure.Persistence.Repositories;

/// <summary>Framework repository with MixPilot's context, owner query filters and persistence error contract.</summary>
public abstract class MixPilotRepository<TEntity>(MixPilotDbContext context, ILogger logger, IMapper mapper)
    : BaseRepository<TEntity, Guid, MixPilotDbContext>(context, logger, mapper)
    where TEntity : OwnedEntity
{
    public override async Task<int> SaveChanges(CancellationToken ct = default)
    {
        try { return await base.SaveChanges(ct); }
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

