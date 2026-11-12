using Framework.Application.Abstractions.Persistence;
using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface ISetRepository : IBaseRepository<DjSet, Guid>
{
    Task<PageResponse<DjSet>> Search(PageQuery query, CancellationToken ct);
}

