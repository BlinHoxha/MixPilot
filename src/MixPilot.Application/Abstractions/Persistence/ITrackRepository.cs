using Framework.Contracts.Pagination;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.Persistence;

public interface ITrackRepository : IOwnedRepository<Track>
{
    Task<TrackVocabulary> GetVocabulary(CancellationToken ct);
    Task<PageResponse<Track>> Search(TrackQuery query, CancellationToken ct);
    Task<IReadOnlyList<Track>> GetCandidates(int limit, CancellationToken ct);
    Task<bool> IdentityExists(string key, Guid? exceptId, CancellationToken ct);
}

public sealed record TrackVocabulary(IReadOnlyList<string> Genres, IReadOnlyList<string> Moods);

