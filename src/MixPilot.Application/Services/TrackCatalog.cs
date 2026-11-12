using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

internal static class TrackCatalog
{
    public static async Task<IReadOnlyList<Track>> Load(ITrackRepository tracks, CancellationToken ct)
    {
        var candidates = await tracks.GetCandidates(5001, ct);
        if (candidates.Count > 5000)
            throw new RuleViolation("library_limit", "This first-release workflow supports up to 5,000 registered tracks.");
        return candidates;
    }
}

