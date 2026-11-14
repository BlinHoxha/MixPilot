using Framework.Contracts.Pagination;
using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.Services;

public interface ISetGenerationJobService
{
    Task<SetGenerationJobResponse> Create(CreateSetGenerationJobRequest request, string correlationId, CancellationToken ct);
    Task<SetGenerationJobResponse> Get(Guid id, CancellationToken ct);
    Task<PageResponse<SetGenerationJobResponse>> Search(PageQuery query, CancellationToken ct);
    Task<SetGenerationJobResponse> Cancel(Guid id, CancellationToken ct);
}

public interface ISetGenerationJobProcessor
{
    Task Process(Guid id, int attempt, CancellationToken ct);
}
