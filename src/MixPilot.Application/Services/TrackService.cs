using Framework.Contracts.Pagination;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

public sealed class TrackService(ITrackRepository repository, ICollectionRepository collections,
    ILogger<TrackService> logger, TimeProvider clock)
    : MixPilotService<Track, ITrackRepository>(repository, logger, clock), ITrackService
{
    public async Task<PageResponse<TrackResponse>> Search(TrackQuery query, CancellationToken ct)
    {
        var page = await EntityRepository.Search(query, ct);
        return PageResponse<TrackResponse>.Create(page.Items.Select(t => t.Response()), page.PageNumber, page.PageSize, page.TotalCount);
    }

    public async Task<TrackResponse> Get(Guid id, CancellationToken ct) => (await RequireTrack(id, ct)).Response();

    public async Task<TrackResponse> Create(CreateTrackRequest request, CancellationToken ct)
    {
        if (!request.AuthorizationConfirmed) throw new RuleViolation("authorization_required", "Track registration requires an authorization declaration.");
        var track = new Track { Id = Guid.NewGuid(), AuthorizationDeclaredAt = Clock.GetUtcNow(), Rights = new() { CanReference = true } };
        Apply(track, request);
        await CheckDuplicate(track, request.KeepDuplicate, ct);
        await PersistNew(track, ct);
        return track.Response();
    }

    public async Task<TrackResponse> Update(Guid id, UpdateTrackRequest request, CancellationToken ct)
    {
        var track = await RequireTrack(id, ct);
        CheckRevision(track, request.ExpectedRevision);
        Apply(track, request);
        await CheckDuplicate(track, request.KeepDuplicate, ct);
        Touch(track);
        await PersistChanges(ct);
        return track.Response();
    }

    public async Task Delete(Guid id, int revision, CancellationToken ct)
    {
        var track = await RequireTrack(id, ct);
        CheckRevision(track, revision);
        if (await collections.ContainsTrack(id, ct)) throw new RuleViolation("resource_in_use", "Remove this track from its collections before deleting it.", 409);
        await RemoveValidated(id, ct);
    }

    public async Task<ImportResponse> Import(CsvImportRequest request, CancellationToken ct)
    {
        if (!request.AuthorizationConfirmed) throw new RuleViolation("authorization_required", "Import requires an authorization declaration.");
        // Parse and validate the entire bounded input before any database mutation.
        var rows = CsvTrackReader.Read(request.Csv);
        var existing = (await TrackCatalog.Load(EntityRepository, ct)).Select(t => t.IdentityKey).ToHashSet(StringComparer.Ordinal);
        var created = new List<Guid>();
        var pending = new List<Track>();
        int skipped = 0;
        foreach (var row in rows)
        {
            var track = new Track { Id = Guid.NewGuid(), AuthorizationDeclaredAt = Clock.GetUtcNow(), Rights = new() { CanReference = true } };
            Apply(track, row);
            if (!existing.Add(track.IdentityKey)) { skipped++; continue; }
            pending.Add(track);
            created.Add(track.Id);
        }
        await PersistNewRange(pending, ct);
        return new(created.Count, skipped, created);
    }

    private async Task CheckDuplicate(Track track, bool keepDuplicate, CancellationToken ct)
    {
        if (!keepDuplicate && await EntityRepository.IdentityExists(track.IdentityKey, track.Id, ct))
            throw new RuleViolation("duplicate_track", "A track with this artist, title and version exists. Set keepDuplicate to retain a separate entry.", 409);
    }

    private async Task<Track> RequireTrack(Guid id, CancellationToken ct) => await EntityRepository.GetById(id, true, ct) ?? throw Missing();

    private static void Apply(Track track, TrackWriteRequest r)
    {
        track.Artist = r.Artist.Trim(); track.Title = r.Title.Trim(); track.Version = r.Version.Trim();
        track.IdentityKey = Track.NormalizeIdentity(track.Artist, track.Title, track.Version);
        track.DurationSeconds = r.DurationSeconds; track.Bpm = r.Bpm; track.MusicalKey = r.MusicalKey;
        track.Energy = r.Energy; track.Genre = r.Genre.Trim(); track.Mood = r.Mood.Trim(); track.HasVocals = r.HasVocals;
    }
}

