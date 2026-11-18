using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface ISetRepository : IOwnedRepository<DjSet>
{
    Task<PageResponse<DjSet>> Search(PageQuery query, CancellationToken ct);
}

