using MixPilot.Domain;

namespace MixPilot.Application.Abstractions.AI;

public interface IDiscoveryProvider
{
    string Name { get; }
    Task<IReadOnlyList<TrackSnapshot>> Discover(string query, DateTimeOffset? publishedAfter, CancellationToken ct);
}

