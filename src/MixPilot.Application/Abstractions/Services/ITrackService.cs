using Framework.Contracts.Pagination;
using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.Services;

public interface ITrackService
{
    Task<PageResponse<TrackResponse>> Search(TrackQuery query, CancellationToken ct);
    Task<TrackResponse> Get(Guid id, CancellationToken ct);
    Task<TrackResponse> Create(CreateTrackRequest request, CancellationToken ct);
    Task<TrackResponse> Update(Guid id, UpdateTrackRequest request, CancellationToken ct);
    Task Delete(Guid id, int revision, CancellationToken ct);
    Task<ImportResponse> Import(CsvImportRequest request, CancellationToken ct);
}

