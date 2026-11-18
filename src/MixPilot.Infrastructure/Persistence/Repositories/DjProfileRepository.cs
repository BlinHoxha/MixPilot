using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Infrastructure.Persistence.Repositories;

public sealed class DjProfileRepository(MixPilotDbContext context, ILogger<DjProfileRepository> logger)
    : MixPilotRepository<DjProfile>(context, logger), IDjProfileRepository
{
    public Task<DjProfile?> GetCurrent(CancellationToken ct) => Query(true).SingleOrDefaultAsync(ct);
}

