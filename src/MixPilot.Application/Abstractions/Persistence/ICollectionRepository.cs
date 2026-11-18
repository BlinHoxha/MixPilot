using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface ICollectionRepository : IOwnedRepository<Collection>
{
    Task<PageResponse<Collection>> Search(PageQuery query, CancellationToken ct);
    Task<bool> ContainsTrack(Guid id, CancellationToken ct);
}

