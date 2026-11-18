using Framework.Contracts.Pagination;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

public sealed class DjProfileService(IDjProfileRepository repository, ILogger<DjProfileService> logger,
    TimeProvider clock)
    : MixPilotService<DjProfile, IDjProfileRepository>(repository, logger, clock), IDjProfileService
{
    public async Task<ProfileResponse> Get(CancellationToken ct) =>
        (await EntityRepository.GetCurrent(ct) ?? throw Missing()).Response();

    public async Task<ProfileResponse> Save(SaveProfileRequest request, CancellationToken ct)
    {
        var profile = await EntityRepository.GetCurrent(ct);
        bool isNew = profile is null;
        if (profile is null)
        {
            if (request.ExpectedRevision != 0) throw new RuleViolation("revision_conflict", "A new profile requires expectedRevision 0.", 409);
            profile = new() { Id = Guid.NewGuid() };
        }
        else { CheckRevision(profile, request.ExpectedRevision); Touch(profile); }
        profile.DisplayName = request.DisplayName.Trim(); profile.PreferredGenre = request.PreferredGenre.Trim();
        profile.MinBpm = request.MinBpm; profile.MaxBpm = request.MaxBpm; profile.PreferVocals = request.PreferVocals;
        if (isNew) await PersistNew(profile, ct);
        else await PersistChanges(ct);
        return profile.Response();
    }

}

