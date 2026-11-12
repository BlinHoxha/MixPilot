using Framework.Contracts.Pagination;
using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.Services;

public interface IDjProfileService
{
    Task<ProfileResponse> Get(CancellationToken ct);
    Task<ProfileResponse> Save(SaveProfileRequest request, CancellationToken ct);
}

