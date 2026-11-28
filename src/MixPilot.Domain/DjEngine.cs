namespace MixPilot.Domain;

/// <summary>Deterministic, bounded greedy planning. Durations use whole tracks, not audio overlaps.</summary>
public sealed class DjEngine
{
    public const int MaxTracks = 200;

    public SetPlan Generate(IEnumerable<TrackSnapshot> candidates, SetRequirements requirements,
        DateTimeOffset now, IReadOnlyList<PlannedTrack>? existing = null)
    {
        var eligible = candidates.Where(t => IsEligible(t, requirements, now)).OrderBy(t => t.Id).ToList();
        var locked = (existing ?? []).Where(t => t.IsLocked).ToDictionary(t => t.Position);
        if (locked.Keys.Any(p => p < 1 || p > MaxTracks) ||
            locked.Values.Select(t => t.Track.Id).Distinct().Count() != locked.Count)
            throw new RuleViolation("invalid_locks", "Locked positions must be unique and within the set limit.");
        foreach (var item in locked.Values)
        {
            if (!eligible.Any(t => t.Id == item.Track.Id))
                throw new RuleViolation("locked_track_unavailable", "A locked track no longer satisfies the current library, rights or musical constraints.");
        }
        int budget = (int)Math.Floor(requirements.DurationSeconds * 1.1);
        if (locked.Values.Sum(t => eligible.Single(e => e.Id == t.Track.Id).DurationSeconds) > budget)
            throw new RuleViolation("locked_duration_exceeded", "Locked tracks exceed the requested duration budget.");
        var output = new List<PlannedTrack>();
        var used = new HashSet<Guid>();
        int elapsed = 0;
        for (int position = 1; position <= MaxTracks; position++)
        {
            TrackSnapshot? previous = output.LastOrDefault()?.Track;
            TrackSnapshot? chosen;
            bool isLocked = locked.TryGetValue(position, out var fixedTrack);
            if (isLocked)
                chosen = eligible.Single(t => t.Id == fixedTrack!.Track.Id);
            else
            {
                bool hasFutureLocks = locked.Keys.Any(p => p > position);
                if (elapsed >= requirements.DurationSeconds && !hasFutureLocks) break;
                int reserved = locked.Where(p => p.Key > position).Sum(p =>
                    eligible.Single(t => t.Id == p.Value.Track.Id).DurationSeconds);
                double progress = Math.Clamp(elapsed / (double)requirements.DurationSeconds, 0, 1);
                double targetEnergy = TargetEnergy(requirements.EnergyCurve, progress);
                chosen = eligible.Where(t => !used.Contains(t.Id) &&
                        !locked.Values.Any(l => l.Track.Id == t.Id) && elapsed + reserved + t.DurationSeconds <= budget)
                    .OrderByDescending(t => 0.65 * Compatibility(previous, t) +
                        0.35 * (1 - Math.Abs(t.Energy!.Value - targetEnergy) / 9.0))
                    .ThenBy(t => t.Id).FirstOrDefault();
                if (chosen is null && hasFutureLocks)
                    throw new RuleViolation("locked_position_unreachable", "Not enough eligible tracks fit before a locked position. Unlock or move that track.");
            }
            if (chosen is null) break;
            used.Add(chosen.Id);
            elapsed += chosen.DurationSeconds;
            output.Add(new(position, chosen, isLocked, Math.Round(Compatibility(previous, chosen), 3),
                previous is null ? ["Opening track selected for the requested energy curve."] :
                [$"BPM difference: {Math.Abs(chosen.Bpm!.Value - previous.Bpm!.Value):0.##}.",
                 HarmonicScore(previous.MusicalKey, chosen.MusicalKey) >= 0.8 ? "Compatible Camelot keys." : "Key compatibility is limited or unknown.",
                 $"Energy {chosen.Energy}/10; {requirements.EnergyCurve} progression."]));
        }
        if (output.Count == 0)
            throw new RuleViolation("no_eligible_tracks", "No tracks with BPM and energy metadata satisfy the duration, rights and musical constraints.");
        var warnings = new List<string>();
        if (elapsed < requirements.DurationSeconds * 0.9)
            warnings.Add("The eligible library cannot fill the requested duration within the current constraints.");
        if (output.Count == MaxTracks) warnings.Add("The 200-track planning limit was reached.");
        if (output.Any(t => t.Track.MusicalKey is null)) warnings.Add("Some tracks lack key metadata; harmonic scoring is incomplete.");
        return new(requirements, output.ToArray(), warnings.ToArray());
    }

    public IReadOnlyList<RankedAlternative> RankAlternatives(SetPlan plan, int position,
        IEnumerable<TrackSnapshot> candidates, DateTimeOffset now, int limit)
    {
        var ordered = plan.Tracks.OrderBy(t => t.Position).ToArray();
        var current = ordered.SingleOrDefault(t => t.Position == position)
            ?? throw new RuleViolation("not_found", "The position was not found.", 404);
        if (current.IsLocked)
            throw new RuleViolation("position_locked", "Unlock the position to get replacement suggestions.");
        var previous = ordered.SingleOrDefault(t => t.Position == position - 1)?.Track;
        var next = ordered.SingleOrDefault(t => t.Position == position + 1)?.Track;
        var used = ordered.Select(t => t.Track.Id).ToHashSet();
        var requirements = plan.Requirements;
        int budget = (int)Math.Floor(requirements.DurationSeconds * 1.1);
        int totalDuration = plan.DurationSeconds;
        double progress = Math.Clamp(ordered.Where(t => t.Position < position).Sum(t => t.Track.DurationSeconds)
            / (double)requirements.DurationSeconds, 0, 1);
        double target = TargetEnergy(requirements.EnergyCurve, progress);

        return candidates.Where(c => !used.Contains(c.Id) && IsEligible(c, requirements, now) &&
                (totalDuration - current.Track.DurationSeconds + c.DurationSeconds <= budget ||
                 c.DurationSeconds <= current.Track.DurationSeconds))
            .Select(c =>
            {
                int neighbours = (previous is null ? 0 : 1) + (next is null ? 0 : 1);
                double transition = neighbours == 0 ? 1 :
                    ((previous is null ? 0 : Compatibility(previous, c)) +
                     (next is null ? 0 : Compatibility(c, next))) / neighbours;
                double energyFit = 1 - Math.Abs(c.Energy!.Value - target) / 9;
                double durationFit = Math.Max(0, 1 - Math.Abs(c.DurationSeconds - current.Track.DurationSeconds)
                    / (double)current.Track.DurationSeconds);
                double score = 0.55 * transition + 0.30 * energyFit + 0.15 * durationFit;
                return (Track: c, Score: score, Transition: transition, EnergyFit: energyFit, DurationFit: durationFit);
            })
            .OrderByDescending(c => c.Score).ThenBy(c => c.Track.Id).Take(limit)
            .Select(c => new RankedAlternative(c.Track, Math.Round(c.Score, 3), Math.Round(c.Transition, 3),
                Math.Round(c.EnergyFit, 3), Math.Round(c.DurationFit, 3),
                AlternativeReasons(c.Track, current.Track, previous, next, target, requirements.EnergyCurve)))
            .ToArray();
    }

    private static IReadOnlyList<string> AlternativeReasons(TrackSnapshot candidate, TrackSnapshot current,
        TrackSnapshot? previous, TrackSnapshot? next, double target, EnergyCurve curve)
    {
        var bpm = new List<string>();
        var keys = new List<string>();
        foreach (var (label, neighbour) in new[] { ("Previous", previous), ("Next", next) })
        {
            if (neighbour is null) continue;
            bpm.Add(neighbour.Bpm is null ? $"{label} BPM difference: unknown." :
                FormattableString.Invariant($"{label} BPM difference: {Math.Abs(candidate.Bpm!.Value - neighbour.Bpm.Value):0.##}."));
            keys.Add($"{label}: " + (HarmonicScore(candidate.MusicalKey, neighbour.MusicalKey) >= 0.8
                ? "Compatible Camelot keys." : "Key compatibility is limited or unknown."));
        }
        var reasons = new List<string>();
        if (bpm.Count > 0) { reasons.Add(string.Join(" ", bpm)); reasons.Add(string.Join(" ", keys)); }
        reasons.Add(FormattableString.Invariant($"Energy {candidate.Energy}/10; target {target:0.#} for {curve}."));
        reasons.Add(FormattableString.Invariant($"Duration difference: {candidate.DurationSeconds - current.DurationSeconds:+0;-0;0} seconds."));
        return reasons;
    }

    public static bool IsEligible(TrackSnapshot t, SetRequirements r, DateTimeOffset now) =>
        t.Rights.Allows(r.Mode, r.Territory, now) && t.Bpm >= r.MinBpm && t.Bpm <= r.MaxBpm &&
        t.Energy is >= 1 and <= 10 && t.DurationSeconds > 0 && !r.ExcludedTrackIds.Contains(t.Id) &&
        (string.IsNullOrWhiteSpace(r.Genre) || string.Equals(t.Genre, r.Genre, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(r.Mood) || string.Equals(t.Mood, r.Mood, StringComparison.OrdinalIgnoreCase)) &&
        (r.HasVocals is null || t.HasVocals == r.HasVocals);

    public static double Compatibility(TrackSnapshot? from, TrackSnapshot to)
    {
        if (from is null) return 1;
        double bpm = from.Bpm is null || to.Bpm is null ? 0 : Math.Max(0, 1 - (double)Math.Abs(from.Bpm.Value - to.Bpm.Value) / 16);
        double energy = from.Energy is null || to.Energy is null ? 0 : 1 - Math.Abs(from.Energy.Value - to.Energy.Value) / 9.0;
        double genre = string.Equals(from.Genre, to.Genre, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return 0.4 * bpm + 0.3 * HarmonicScore(from.MusicalKey, to.MusicalKey) + 0.2 * energy + 0.1 * genre;
    }

    public static double HarmonicScore(string? first, string? second)
    {
        if (!TryKey(first, out int a, out char modeA) || !TryKey(second, out int b, out char modeB)) return 0;
        if (a == b && modeA == modeB) return 1;
        if (a == b || modeA == modeB && (Math.Abs(a - b) == 1 || Math.Abs(a - b) == 11)) return 0.85;
        return 0.15;
    }

    private static bool TryKey(string? key, out int number, out char mode)
    {
        number = 0; mode = ' ';
        if (key is null || key.Length is < 2 or > 3) return false;
        mode = char.ToUpperInvariant(key[^1]);
        return mode is 'A' or 'B' && int.TryParse(key[..^1], out number) && number is >= 1 and <= 12;
    }

    private static double TargetEnergy(EnergyCurve curve, double progress) => curve switch
    {
        EnergyCurve.WarmUp => 2 + 3 * progress,
        EnergyCurve.HighEnergy => 8.5,
        EnergyCurve.Wave => 5.5 + 2.5 * Math.Sin(progress * Math.PI * 4 - Math.PI / 2),
        EnergyCurve.PeakAndCooldown => progress < 0.7 ? 3 + progress / 0.7 * 6 : 9 - (progress - 0.7) / 0.3 * 5,
        _ => 3 + 6 * progress
    };
}
