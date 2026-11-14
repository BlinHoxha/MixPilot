using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface ISetGenerationJobRepository
{
    Task Add(SetGenerationJob job, CancellationToken ct);
    Task<SetGenerationJob?> Get(Guid id, CancellationToken ct);
    Task<PageResponse<SetGenerationJob>> Search(PageQuery query, CancellationToken ct);
    Task<SetGenerationJob> Cancel(Guid id, DateTimeOffset now, CancellationToken ct);
    Task<bool> CancelIfRequested(Guid id, int attempt, DateTimeOffset now, CancellationToken ct);
    Task SetProgress(Guid id, int attempt, int progress, DateTimeOffset now, CancellationToken ct);
    Task Finish(Guid id, int attempt, string status, string? code, Guid? setId, DateTimeOffset now, CancellationToken ct);
    Task<bool> RenewLease(Guid id, int attempt, DateTimeOffset now, CancellationToken ct);
}

public sealed record JobClaim(Guid Id, string OwnerKey, int Attempt, bool Exhausted);

public interface IJobClaimRepository
{
    Task<JobClaim?> ClaimNext(DateTimeOffset now, CancellationToken ct);
}
