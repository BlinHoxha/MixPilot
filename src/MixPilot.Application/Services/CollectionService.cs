using Framework.Contracts.Pagination;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

public sealed class CollectionService(ICollectionRepository repository, ITrackRepository tracks,
    ILogger<CollectionService> logger, TimeProvider clock)
    : MixPilotService<Collection, ICollectionRepository>(repository, logger, clock), ICollectionService
{
    public async Task<CollectionResponse> Create(SaveCollectionRequest request, CancellationToken ct)
    {
        if (request.ExpectedRevision != 0) throw new RuleViolation("revision_conflict", "A new collection requires expectedRevision 0.", 409);
        await CheckTrackIds(request.TrackIds, ct);
        var collection = new Collection { Id = Guid.NewGuid(), Name = request.Name.Trim() };
        collection.Tracks = request.TrackIds.Select((id, i) => new CollectionTrack { CollectionId = collection.Id, TrackId = id, Position = i + 1 }).ToList();
        await PersistNew(collection, ct);
        return collection.Response();
    }

    public async Task<CollectionResponse> Update(Guid id, SaveCollectionRequest request, CancellationToken ct)
    {
        var collection = await EntityRepository.GetById(id, true, ct) ?? throw Missing();
        CheckRevision(collection, request.ExpectedRevision);
        await CheckTrackIds(request.TrackIds, ct);
        collection.Name = request.Name.Trim();
        collection.Tracks.RemoveAll(t => !request.TrackIds.Contains(t.TrackId));
        for (int i = 0; i < request.TrackIds.Length; i++)
        {
            var membership = collection.Tracks.SingleOrDefault(t => t.TrackId == request.TrackIds[i]);
            if (membership is null) collection.Tracks.Add(new() { CollectionId = id, TrackId = request.TrackIds[i], Position = i + 1 });
            else membership.Position = i + 1;
        }
        Touch(collection);
        await PersistChanges(ct);
        return collection.Response();
    }

    public async Task<PageResponse<CollectionResponse>> Search(PageQuery query, CancellationToken ct)
    {
        var page = await EntityRepository.Search(query, ct);
        return PageResponse<CollectionResponse>.Create(page.Items.Select(c => c.Response()), page.PageNumber, page.PageSize, page.TotalCount);
    }

    public async Task<CollectionResponse> Get(Guid id, CancellationToken ct) =>
        (await EntityRepository.GetById(id, true, ct) ?? throw Missing()).Response();

    public async Task Delete(Guid id, int revision, CancellationToken ct)
    {
        var collection = await EntityRepository.GetById(id, true, ct) ?? throw Missing();
        CheckRevision(collection, revision); await RemoveValidated(id, ct);
    }

    private async Task CheckTrackIds(Guid[] ids, CancellationToken ct)
    {
        if (ids.Distinct().Count() != ids.Length) throw new RuleViolation("duplicate_track", "A collection cannot contain duplicate track IDs.");
        foreach (var id in ids) _ = await tracks.GetById(id, false, ct) ?? throw Missing();
    }

}

