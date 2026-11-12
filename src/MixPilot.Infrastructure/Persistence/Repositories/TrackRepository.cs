using AutoMapper;
using Framework.Contracts.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Infrastructure.Persistence.Repositories;

public sealed class TrackRepository(MixPilotDbContext context, ILogger<TrackRepository> logger, IMapper mapper)
    : MixPilotRepository<Track>(context, logger, mapper), ITrackRepository
{
    public async Task<TrackVocabulary> GetVocabulary(CancellationToken ct)
    {
        var genres = await Query().Where(t => t.Genre != "").Select(t => t.Genre)
            .Distinct().OrderBy(value => value).Take(200).ToArrayAsync(ct);
        var moods = await Query().Where(t => t.Mood != "").Select(t => t.Mood)
            .Distinct().OrderBy(value => value).Take(200).ToArrayAsync(ct);
        return new(genres, moods);
    }

    public async Task<PageResponse<Track>> Search(TrackQuery query, CancellationToken ct)
    {
        var tracks = Query();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim().ToUpperInvariant();
            tracks = tracks.Where(t => t.Artist.ToUpper().Contains(term) || t.Title.ToUpper().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(query.Genre))
        {
            string genre = query.Genre.Trim().ToUpperInvariant(); tracks = tracks.Where(t => t.Genre.ToUpper() == genre);
        }
        if (query.MinBpm is { } min) tracks = tracks.Where(t => t.Bpm >= min);
        if (query.MaxBpm is { } max) tracks = tracks.Where(t => t.Bpm <= max);
        int count = await tracks.CountAsync(ct);
        bool asc = query.Direction == OrderDirection.Asc;
        IOrderedQueryable<Track> ordered = query.SortBy switch
        {
            TrackSort.Title => asc ? tracks.OrderBy(t => t.Title) : tracks.OrderByDescending(t => t.Title),
            TrackSort.Artist => asc ? tracks.OrderBy(t => t.Artist) : tracks.OrderByDescending(t => t.Artist),
            TrackSort.Bpm => asc ? tracks.OrderBy(t => t.Bpm) : tracks.OrderByDescending(t => t.Bpm),
            _ => asc ? tracks.OrderBy(t => t.CreatedAt) : tracks.OrderByDescending(t => t.CreatedAt)
        };
        var items = await ordered.ThenBy(t => t.Id).Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return PageResponse<Track>.Create(items, query.PageNumber, query.PageSize, count);
    }


    public Task<bool> IdentityExists(string key, Guid? exceptId, CancellationToken ct) =>
        Query().AnyAsync(t => t.IdentityKey == key && t.Id != exceptId, ct);
    public async Task<IReadOnlyList<Track>> GetCandidates(int limit, CancellationToken ct) =>
        await Query().OrderBy(t => t.Id).Take(limit).ToArrayAsync(ct);
}

