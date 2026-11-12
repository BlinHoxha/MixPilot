using AutoMapper;
using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Infrastructure.Persistence.Repositories;

public sealed class SetRepository(MixPilotDbContext context, ILogger<SetRepository> logger, IMapper mapper)
    : MixPilotRepository<DjSet>(context, logger, mapper), ISetRepository
{
    public override IQueryable<DjSet> Query(bool track = false) => base.Query(track).Include(s => s.Versions);
    public Task<PageResponse<DjSet>> Search(PageQuery query, CancellationToken ct) => Page(base.Query(), query, ct);
}

