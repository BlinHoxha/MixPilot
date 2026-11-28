using Framework.Domain.Abstractions;

namespace MixPilot.Domain;

public abstract class OwnedEntity : BaseEntity<Guid>
{
    public string OwnerKey { get; set; } = "";
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SourceKind { MyLibrary, ExternalReference, LicensedCatalog, PlatformOriginal }
public enum SetMode { Planning, Mixable }
public enum EnergyCurve { GradualBuild, Wave, PeakAndCooldown, HighEnergy, WarmUp }

public sealed record RightsProfile
{
    public bool CanReference { get; init; }
    public bool CanAnalyze { get; init; }
    public bool CanStream { get; init; }
    public bool CanDownload { get; init; }
    public bool CanTempoShift { get; init; }
    public bool CanKeyShift { get; init; }
    public bool CanMix { get; init; }
    public bool CanExportAudio { get; init; }
    public bool CanPublish { get; init; }
    public string[] Territories { get; init; } = [];
    public DateTimeOffset? LicenseStart { get; init; }
    public DateTimeOffset? LicenseEnd { get; init; }

    public bool Allows(SetMode mode, string territory, DateTimeOffset now) =>
        CanReference && (mode == SetMode.Planning || CanMix) &&
        (LicenseStart is null || now >= LicenseStart) &&
        (LicenseEnd is null || now < LicenseEnd) &&
        (Territories.Length == 0 || Territories.Contains(territory, StringComparer.OrdinalIgnoreCase));
}

public sealed class Track : OwnedEntity
{
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public string Album { get; set; } = "";
    public string Label { get; set; } = "";
    public DateOnly? ReleaseDate { get; set; }
    public string Subgenre { get; set; } = "";
    public string TagsStorage { get; set; } = "";
    public string[] Tags
    {
        get => TagsStorage.Split('|', StringSplitOptions.RemoveEmptyEntries);
        set => TagsStorage = value.Length == 0 ? "" : "|" + string.Join('|', value) + "|";
    }
    public string IdentityKey { get; set; } = "";
    public int DurationSeconds { get; set; }
    public decimal? Bpm { get; set; }
    public string? MusicalKey { get; set; }
    public int? Energy { get; set; }
    public string Genre { get; set; } = "";
    public string Mood { get; set; } = "";
    public bool? HasVocals { get; set; }
    public SourceKind Source { get; set; } = SourceKind.MyLibrary;
    public string? SourceUrl { get; set; }
    public DateTimeOffset? AuthorizationDeclaredAt { get; set; }
    public RightsProfile Rights { get; set; } = new();

    public static string NormalizeIdentity(string artist, string title, string version) =>
        string.Join("|", new[] { artist, title, version }.Select(x =>
            string.Join(' ', x.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));

    public TrackSnapshot Snapshot() => new(Id, Artist, Title, Version, DurationSeconds, Bpm,
        MusicalKey, Energy, Genre, Mood, HasVocals, Source, SourceUrl, Rights);
}

public sealed record TrackSnapshot(Guid Id, string Artist, string Title, string Version,
    int DurationSeconds, decimal? Bpm, string? MusicalKey, int? Energy, string Genre,
    string Mood, bool? HasVocals, SourceKind Source, string? SourceUrl, RightsProfile Rights);

public sealed class Collection : OwnedEntity
{
    public string Name { get; set; } = "";
    public List<CollectionTrack> Tracks { get; set; } = [];
}

public sealed class CollectionTrack
{
    public Guid CollectionId { get; set; }
    public Guid TrackId { get; set; }
    public int Position { get; set; }
}

public sealed class DjProfile : OwnedEntity
{
    public string DisplayName { get; set; } = "";
    public string PreferredGenre { get; set; } = "";
    public decimal MinBpm { get; set; } = 110;
    public decimal MaxBpm { get; set; } = 140;
    public bool? PreferVocals { get; set; }
}
