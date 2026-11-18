using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Infrastructure.Persistence.Repositories;

public sealed class CollectionRepository(MixPilotDbContext context, ILogger<CollectionRepository> logger)
    : MixPilotRepository<Collection>(context, logger), ICollectionRepository
{
    public override IQueryable<Collection> Query(bool track = false) => base.Query(track).Include(c => c.Tracks);
    public Task<PageResponse<Collection>> Search(PageQuery query, CancellationToken ct) => Page(Query(), query, ct);
    public Task<bool> ContainsTrack(Guid id, CancellationToken ct) => Query().AnyAsync(c => c.Tracks.Any(t => t.TrackId == id), ct);
}

