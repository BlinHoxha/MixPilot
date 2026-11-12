using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application;

public static class ContractMapping
{
    public static RightsResponse Response(this RightsProfile r) => new(r.CanReference, r.CanAnalyze,
        r.CanStream, r.CanDownload, r.CanTempoShift, r.CanKeyShift, r.CanMix, r.CanExportAudio,
        r.CanPublish, r.Territories, r.LicenseStart, r.LicenseEnd);

    public static TrackResponse Response(this Track t) => new(t.Id, t.Revision, t.Artist, t.Title, t.Version,
        t.DurationSeconds, t.Bpm, t.MusicalKey, t.Energy, t.Genre, t.Mood, t.HasVocals,
        (SourceType)t.Source, Availability(t.Source), t.SourceUrl, t.Rights.Response(), t.CreatedAt, t.UpdatedAt);

    public static string Availability(SourceKind source) => source == SourceKind.MyLibrary ? "registeredMetadata" : "externalReference";

    public static SetRequirements Domain(this SetRequirementsRequest r) => new(r.DurationSeconds,
        r.MinBpm, r.MaxBpm, r.Genre?.Trim(), r.Mood?.Trim(), r.HasVocals, (EnergyCurve)r.EnergyCurve,
        (SetMode)r.Mode, r.Territory, r.ExcludedTrackIds);

    public static SetRequirementsRequest Response(this SetRequirements r) => new()
    {
        DurationSeconds = r.DurationSeconds, MinBpm = r.MinBpm, MaxBpm = r.MaxBpm,
        Genre = r.Genre, Mood = r.Mood, HasVocals = r.HasVocals,
        EnergyCurve = (EnergyCurveType)r.EnergyCurve, Mode = (PlanningMode)r.Mode,
        Territory = r.Territory, ExcludedTrackIds = r.ExcludedTrackIds
    };

    public static CollectionResponse Response(this Collection c) => new(c.Id, c.Revision, c.Name,
        c.Tracks.OrderBy(t => t.Position).Select(t => t.TrackId).ToArray());

    public static ProfileResponse Response(this DjProfile p) => new(p.Revision, p.DisplayName,
        p.PreferredGenre, p.MinBpm, p.MaxBpm, p.PreferVocals);

    public static SetResponse Response(this DjSet s, SetVersion v, SetPlan plan) => new(s.Id, s.Name,
        s.Revision, v.Number, plan.Requirements.Response(), plan.DurationSeconds,
        plan.Tracks.Select(p => new SetTrackResponse(p.Position, p.Track.Id, p.Track.Artist,
            p.Track.Title, p.Track.Version, p.Track.DurationSeconds, p.Track.Bpm, p.Track.MusicalKey,
            p.Track.Energy, (SourceType)p.Track.Source, Availability(p.Track.Source), p.IsLocked,
            p.Compatibility, p.Reasons, p.Track.Rights.Response())).ToArray(), plan.Warnings, v.CreatedAt);
}
