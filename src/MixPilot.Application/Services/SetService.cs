using System.Text.Json;
using AutoMapper;
using Framework.Contracts.Pagination;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

public sealed class SetService(ISetRepository repository, ITrackRepository trackRepository, DjEngine engine,
    ILogger<SetService> logger, IMapper mapper, TimeProvider clock)
    : MixPilotService<DjSet, ISetRepository>(repository, logger, mapper, clock), ISetService
{
    public async Task<SetResponse> Generate(GenerateSetRequest request, CancellationToken ct)
    {
        var plan = engine.Generate((await TrackCatalog.Load(trackRepository, ct)).Select(t => t.Snapshot()), request.Requirements.Domain(), Clock.GetUtcNow());
        var set = new DjSet { Id = Guid.NewGuid(), Name = request.Name.Trim() };
        AddVersion(set, plan); await PersistNew(set, ct);
        return Response(set, set.Versions.Single(), plan);
    }

    public async Task<SetResponse> Get(Guid id, int? version, CancellationToken ct)
    {
        var set = await Require(id, ct);
        var selected = version is null ? set.Versions.MaxBy(v => v.Number)! : set.Versions.SingleOrDefault(v => v.Number == version) ?? throw Missing();
        return Response(set, selected, Read(selected));
    }

    public async Task<PageResponse<SetSummaryResponse>> Search(PageQuery query, CancellationToken ct)
    {
        var page = await EntityRepository.Search(query, ct);
        return PageResponse<SetSummaryResponse>.Create(page.Items.Select(s => new SetSummaryResponse(s.Id, s.Name, s.Revision, s.UpdatedAt)), page.PageNumber, page.PageSize, page.TotalCount);
    }

    public async Task<IReadOnlyList<SetVersionResponse>> Versions(Guid id, CancellationToken ct)
    {
        var set = await Require(id, ct);
        return set.Versions.OrderByDescending(v => v.Number).Select(v => new SetVersionResponse(v.Number, v.CreatedAt)
        {
            Status = Status(set, v), ApprovedAt = v.ApprovedAt
        }).ToArray();
    }

    public async Task<SetResponse> Approve(Guid id, int version, ApproveSetVersionRequest request, CancellationToken ct)
    {
        var set = await Require(id, ct);
        var selected = set.Versions.SingleOrDefault(v => v.Number == version) ?? throw Missing();
        CheckRevision(set, request.ExpectedRevision);
        var plan = Read(selected);
        if (set.ApprovedVersion == version) return Response(set, selected, plan);

        var now = Clock.GetUtcNow();
        foreach (var planned in plan.Tracks)
        {
            var track = await trackRepository.GetById(planned.Track.Id, false, ct);
            if (track is null || !DjEngine.IsEligible(track.Snapshot(), plan.Requirements, now))
                throw new RuleViolation("track_ineligible", "A track no longer satisfies the set requirements or rights.");
        }

        Touch(set);
        set.ApprovedVersion = version;
        selected.ApprovedAt ??= now;
        await PersistChanges(ct);
        return Response(set, selected, plan);
    }

    public async Task<SetResponse> Regenerate(Guid id, RegenerateSetRequest request, CancellationToken ct)
    {
        var set = await Require(id, ct); CheckRevision(set, request.ExpectedRevision);
        var latest = Read(set.Versions.MaxBy(v => v.Number)!);
        var plan = engine.Generate((await TrackCatalog.Load(trackRepository, ct)).Select(t => t.Snapshot()), request.Requirements.Domain(), Clock.GetUtcNow(), latest.Tracks);
        return await SaveVersion(set, plan, ct);
    }

    public async Task<SetResponse> Edit(Guid id, EditSetRequest request, CancellationToken ct)
    {
        var set = await Require(id, ct); CheckRevision(set, request.ExpectedRevision);
        var current = Read(set.Versions.MaxBy(v => v.Number)!);
        if (request.OrderedTrackIds.Distinct().Count() != request.OrderedTrackIds.Length ||
            request.LockedTrackIds.Distinct().Count() != request.LockedTrackIds.Length ||
            request.LockedTrackIds.Except(request.OrderedTrackIds).Any())
            throw new RuleViolation("invalid_track_order", "Track IDs must be unique; every locked track must be in the set.");
        var tracks = new List<PlannedTrack>();
        foreach (var trackId in request.OrderedTrackIds)
        {
            var track = await trackRepository.GetById(trackId, false, ct) ?? throw Missing();
            if (!DjEngine.IsEligible(track.Snapshot(), current.Requirements, Clock.GetUtcNow()))
                throw new RuleViolation("track_ineligible", "A selected track does not satisfy the set requirements or rights.");
            var snapshot = track.Snapshot();
            tracks.Add(new(tracks.Count + 1, snapshot, request.LockedTrackIds.Contains(trackId),
                Math.Round(DjEngine.Compatibility(tracks.LastOrDefault()?.Track, snapshot), 3), ["Manually selected order."]));
        }
        int duration = tracks.Sum(t => t.Track.DurationSeconds);
        string[] warnings = Math.Abs(duration - current.Requirements.DurationSeconds) > current.Requirements.DurationSeconds * 0.1
            ? ["The manually edited duration differs from the target by more than 10%."] : [];
        return await SaveVersion(set, new(current.Requirements, tracks.ToArray(), warnings), ct);
    }

    private async Task<SetResponse> SaveVersion(DjSet set, SetPlan plan, CancellationToken ct)
    {
        set.Revision++; set.UpdatedAt = Clock.GetUtcNow(); AddVersion(set, plan);
        await PersistChanges(ct); return Response(set, set.Versions.MaxBy(v => v.Number)!, plan);
    }

    private void AddVersion(DjSet set, SetPlan plan) => set.Versions.Add(new()
    {
        SetId = set.Id, Number = set.Versions.Count + 1, CreatedAt = Clock.GetUtcNow(), PlanJson = JsonSerializer.Serialize(plan)
    });
    private static SetPlan Read(SetVersion version) => JsonSerializer.Deserialize<SetPlan>(version.PlanJson)!;
    private static SetVersionStatus Status(DjSet set, SetVersion version) =>
        set.ApprovedVersion == version.Number ? SetVersionStatus.Approved :
        version.ApprovedAt is not null ? SetVersionStatus.Superseded : SetVersionStatus.Draft;
    private static SetResponse Response(DjSet set, SetVersion version, SetPlan plan) =>
        set.Response(version, plan) with { Status = Status(set, version), ApprovedVersion = set.ApprovedVersion };
    private async Task<DjSet> Require(Guid id, CancellationToken ct) => await EntityRepository.GetById(id, true, ct) ?? throw Missing();
}

