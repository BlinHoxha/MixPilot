using Framework.Application.Abstractions.Persistence;
using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface IDjProfileRepository : IBaseRepository<DjProfile, Guid>
{
    Task<DjProfile?> GetCurrent(CancellationToken ct);
}

