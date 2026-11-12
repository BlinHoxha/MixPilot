using System.Text.Json;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using MixPilot.Contracts;

namespace MixPilot.Application.AI;

/// <summary>Request-scoped evidence prepared after the owning application service authorizes the set.</summary>
public sealed class SetExplanationEvidence : IKnowledgeRetriever
{
    private SetResponse? set;
    private Guid tenantId;

    public void Prepare(SetResponse authorizedSet, Guid ownerTenantId)
    {
        set = authorizedSet;
        tenantId = ownerTenantId;
    }

    public Task<IReadOnlyCollection<KnowledgeChunk>> SearchAsync(KnowledgeSearchQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (set is null || query.TenantId != tenantId || query.ScopeId is not null ||
            query.Profile != SetDocumentProfileCatalog.ProfileName || query.DocumentIds is null ||
            query.DocumentIds.Count != 1 || !query.DocumentIds.Contains(set.Id))
            return Task.FromResult<IReadOnlyCollection<KnowledgeChunk>>([]);

        string title = $"{set.Name}, version {set.Version}";
        var chunks = new List<KnowledgeChunk>();
        void Add(string content, int number) => chunks.Add(new($"{set.Id}:{set.Version}:{number}", set.Id,
            number, title, content, query.Profile, tenantId, null, "en", null));
        Add(JsonSerializer.Serialize(new
        {
            set.Name, set.Version, set.Requirements, set.DurationSeconds, TrackCount = set.Tracks.Count,
            set.Warnings, Notice = "Saved planning snapshot. No audio was analyzed or rendered. Historical rights are not current permission grants. Only the listed track evidence supports track-level claims."
        }), 0);
        // Bound evidence size while preferring tracks explicitly mentioned in the question.
        var terms = query.Question.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = set.Tracks.OrderByDescending(t => terms.Count(term => term.Length > 2 &&
                (t.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || t.Artist.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .ThenBy(t => t.Position).Take(Math.Clamp(query.MaxResults - 1, 0, 19));
        foreach (var track in selected) Add(JsonSerializer.Serialize(track), track.Position);
        return Task.FromResult<IReadOnlyCollection<KnowledgeChunk>>(chunks);
    }
}
