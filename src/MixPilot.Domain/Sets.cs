namespace MixPilot.Domain;

public sealed record SetRequirements(int DurationSeconds, decimal MinBpm, decimal MaxBpm,
    string? Genre, string? Mood, bool? HasVocals, EnergyCurve EnergyCurve, SetMode Mode,
    string Territory, Guid[] ExcludedTrackIds);

public sealed record PlannedTrack(int Position, TrackSnapshot Track, bool IsLocked,
    double Compatibility, string[] Reasons);

public sealed record RankedAlternative(TrackSnapshot Track, double Score, double Transition,
    double EnergyFit, double DurationFit, IReadOnlyList<string> Reasons);

public sealed record SetPlan(SetRequirements Requirements, PlannedTrack[] Tracks, string[] Warnings)
{
    public int DurationSeconds => Tracks.Sum(t => t.Track.DurationSeconds);
}

public sealed class DjSet : OwnedEntity
{
    public string Name { get; set; } = "";
    public int? ApprovedVersion { get; set; }
    public List<SetVersion> Versions { get; set; } = [];
}

public sealed class SetVersion
{
    public Guid SetId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; }
    public string PlanJson { get; set; } = "";
}

public sealed class RuleViolation(string code, string message, int statusCode = 422) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
