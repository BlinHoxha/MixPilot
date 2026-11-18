using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface IDjProfileRepository : IOwnedRepository<DjProfile>
{
    Task<DjProfile?> GetCurrent(CancellationToken ct);
}

