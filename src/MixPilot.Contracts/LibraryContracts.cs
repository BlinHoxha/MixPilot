using System.ComponentModel.DataAnnotations;

namespace MixPilot.Contracts;

public enum SourceType { MyLibrary, ExternalReference, LicensedCatalog, PlatformOriginal }
public enum PlanningMode { Planning, Mixable }
public enum EnergyCurveType { GradualBuild, Wave, PeakAndCooldown, HighEnergy, WarmUp }
public enum TrackSort { Title, Artist, Bpm, CreatedAt }
public enum OrderDirection { Asc, Desc }

public class TrackWriteRequest
{
    [Required, StringLength(200)] public string Artist { get; init; } = "";
    [Required, StringLength(300)] public string Title { get; init; } = "";
    [StringLength(200)] public string Version { get; init; } = "";
    [Range(1, 7200)] public int DurationSeconds { get; init; }
    [Range(20, 300)] public decimal? Bpm { get; init; }
    [RegularExpression("^(?:[1-9]|1[0-2])[AB]$")] public string? MusicalKey { get; init; }
    [Range(1, 10)] public int? Energy { get; init; }
    [StringLength(100)] public string Genre { get; init; } = "";
    [StringLength(100)] public string Mood { get; init; } = "";
    public bool? HasVocals { get; init; }
}

public sealed class CreateTrackRequest : TrackWriteRequest, IValidatableObject
{
    public bool AuthorizationConfirmed { get; init; }
    public bool KeepDuplicate { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!AuthorizationConfirmed) yield return new("Confirm that you are authorized to register this personal-library track.", [nameof(AuthorizationConfirmed)]);
    }
}

public sealed class UpdateTrackRequest : TrackWriteRequest
{
    [Range(1, int.MaxValue)] public int ExpectedRevision { get; init; }
    public bool KeepDuplicate { get; init; }
}

public class PageQuery
{
    [Range(1, 100000)] public int PageNumber { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class TrackQuery : PageQuery, IValidatableObject
{
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(100)] public string? Genre { get; init; }
    [Range(20, 300)] public decimal? MinBpm { get; init; }
    [Range(20, 300)] public decimal? MaxBpm { get; init; }
    [EnumDataType(typeof(TrackSort))] public TrackSort SortBy { get; init; } = TrackSort.CreatedAt;
    [EnumDataType(typeof(OrderDirection))] public OrderDirection Direction { get; init; } = OrderDirection.Desc;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (MinBpm > MaxBpm) yield return new("MinBpm cannot exceed MaxBpm.", [nameof(MinBpm)]);
    }
}

public sealed record RightsResponse(bool CanReference, bool CanAnalyze, bool CanStream,
    bool CanDownload, bool CanTempoShift, bool CanKeyShift, bool CanMix, bool CanExportAudio,
    bool CanPublish, IReadOnlyList<string> Territories, DateTimeOffset? LicenseStart, DateTimeOffset? LicenseEnd);

public sealed record TrackResponse(Guid Id, int Revision, string Artist, string Title, string Version,
    int DurationSeconds, decimal? Bpm, string? MusicalKey, int? Energy, string Genre, string Mood,
    bool? HasVocals, SourceType Source, string Availability, string? SourceUrl, RightsResponse Rights,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed class SaveCollectionRequest
{
    [Required, StringLength(150)] public string Name { get; init; } = "";
    [Required, MaxLength(1000)] public Guid[] TrackIds { get; init; } = [];
    [Range(0, int.MaxValue)] public int ExpectedRevision { get; init; }
}

public sealed record CollectionResponse(Guid Id, int Revision, string Name, IReadOnlyList<Guid> TrackIds);

public sealed class SaveProfileRequest : IValidatableObject
{
    [Required, StringLength(100)] public string DisplayName { get; init; } = "";
    [StringLength(100)] public string PreferredGenre { get; init; } = "";
    [Range(20, 300)] public decimal MinBpm { get; init; } = 110;
    [Range(20, 300)] public decimal MaxBpm { get; init; } = 140;
    public bool? PreferVocals { get; init; }
    [Range(0, int.MaxValue)] public int ExpectedRevision { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (MinBpm > MaxBpm) yield return new("MinBpm cannot exceed MaxBpm.", [nameof(MinBpm)]);
    }
}

public sealed record ProfileResponse(int Revision, string DisplayName, string PreferredGenre,
    decimal MinBpm, decimal MaxBpm, bool? PreferVocals);

public sealed class CsvImportRequest : IValidatableObject
{
    [Required, StringLength(500000)] public string Csv { get; init; } = "";
    public bool AuthorizationConfirmed { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!AuthorizationConfirmed) yield return new("Confirm authorization to register the imported tracks.", [nameof(AuthorizationConfirmed)]);
    }
}

public sealed record ImportResponse(int CreatedCount, int SkippedDuplicateCount, IReadOnlyList<Guid> CreatedTrackIds);
