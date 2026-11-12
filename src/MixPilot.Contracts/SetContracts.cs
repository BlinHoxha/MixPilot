using System.ComponentModel.DataAnnotations;

namespace MixPilot.Contracts;

public sealed class SetRequirementsRequest : IValidatableObject
{
    [Range(60, 28800)] public int DurationSeconds { get; init; } = 3600;
    [Range(20, 300)] public decimal MinBpm { get; init; } = 110;
    [Range(20, 300)] public decimal MaxBpm { get; init; } = 140;
    [StringLength(100)] public string? Genre { get; init; }
    [StringLength(100)] public string? Mood { get; init; }
    public bool? HasVocals { get; init; }
    [EnumDataType(typeof(EnergyCurveType))] public EnergyCurveType EnergyCurve { get; init; } = EnergyCurveType.GradualBuild;
    [EnumDataType(typeof(PlanningMode))] public PlanningMode Mode { get; init; } = PlanningMode.Planning;
    [Required, RegularExpression("^[A-Z]{2}$")] public string Territory { get; init; } = "HU";
    [Required, MaxLength(1000)] public Guid[] ExcludedTrackIds { get; init; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (MinBpm > MaxBpm) yield return new("MinBpm cannot exceed MaxBpm.", [nameof(MinBpm)]);
    }
}

public sealed class GenerateSetRequest
{
    [Required, StringLength(150)] public string Name { get; init; } = "";
    [Required] public SetRequirementsRequest Requirements { get; init; } = new();
}

public sealed class RegenerateSetRequest
{
    [Range(1, int.MaxValue)] public int ExpectedRevision { get; init; }
    [Required] public SetRequirementsRequest Requirements { get; init; } = new();
}

public sealed class EditSetRequest
{
    [Range(1, int.MaxValue)] public int ExpectedRevision { get; init; }
    [Required, MinLength(1), MaxLength(200)] public Guid[] OrderedTrackIds { get; init; } = [];
    [Required, MaxLength(200)] public Guid[] LockedTrackIds { get; init; } = [];
}

public sealed class ApproveSetVersionRequest
{
    [Range(1, int.MaxValue)] public int ExpectedRevision { get; init; }
}

public enum SetVersionStatus { Draft, Approved, Superseded }

public sealed record SetTrackResponse(int Position, Guid TrackId, string Artist, string Title,
    string Version, int DurationSeconds, decimal? Bpm, string? MusicalKey, int? Energy,
    SourceType Source, string Availability, bool IsLocked, double Compatibility,
    IReadOnlyList<string> Reasons, RightsResponse Rights);

public sealed record SetResponse(Guid Id, string Name, int Revision, int Version,
    SetRequirementsRequest Requirements, int DurationSeconds, IReadOnlyList<SetTrackResponse> Tracks,
    IReadOnlyList<string> Warnings, DateTimeOffset UpdatedAt)
{
    public SetVersionStatus Status { get; init; }
    public int? ApprovedVersion { get; init; }
}

public sealed record SetSummaryResponse(Guid Id, string Name, int Revision, DateTimeOffset UpdatedAt);
public sealed record SetVersionResponse(int Version, DateTimeOffset CreatedAt)
{
    public SetVersionStatus Status { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
}
public sealed record CapabilitiesResponse(string ApiVersion, string[] EnabledFeatures, string[] PlannedFeatures,
    int MaximumPageSize, int MaximumCsvRows, bool AudioOperationsAvailable, bool PromptInterpretationAvailable);
