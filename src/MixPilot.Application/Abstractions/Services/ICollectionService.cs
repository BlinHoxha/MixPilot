using Framework.Contracts.Pagination;
using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.Services;

public interface ICollectionService
{
    Task<PageResponse<CollectionResponse>> Search(PageQuery query, CancellationToken ct);
    Task<CollectionResponse> Get(Guid id, CancellationToken ct);
    Task<CollectionResponse> Create(SaveCollectionRequest request, CancellationToken ct);
    Task<CollectionResponse> Update(Guid id, SaveCollectionRequest request, CancellationToken ct);
    Task Delete(Guid id, int revision, CancellationToken ct);
}

