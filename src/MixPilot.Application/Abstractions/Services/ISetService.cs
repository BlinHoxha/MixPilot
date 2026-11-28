using Framework.Contracts.Pagination;
using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.Services;

public interface ISetService
{
    Task<SetResponse> Generate(GenerateSetRequest request, CancellationToken ct);
    Task<SetResponse> Get(Guid id, int? version, CancellationToken ct);
    Task<SetAlternativesResponse> Alternatives(Guid id, int position, int limit, CancellationToken ct);
    Task<PageResponse<SetSummaryResponse>> Search(PageQuery query, CancellationToken ct);
    Task<IReadOnlyList<SetVersionResponse>> Versions(Guid id, CancellationToken ct);
    Task<SetResponse> Regenerate(Guid id, RegenerateSetRequest request, CancellationToken ct);
    Task<SetResponse> Edit(Guid id, EditSetRequest request, CancellationToken ct);
    Task<SetResponse> Approve(Guid id, int version, ApproveSetVersionRequest request, CancellationToken ct);
}

