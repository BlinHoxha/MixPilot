using System.Globalization;
using MixPilot.Domain;

namespace MixPilot.Tests;

public sealed class AlternativesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly DjEngine engine = new();
    private static SetRequirements Requirements(int duration = 900) =>
        new(duration, 120, 140, "House", "Focused", false, EnergyCurve.GradualBuild, SetMode.Planning, "HU", []);
    private static TrackSnapshot Track(int id) => new(new Guid(id, 0, 0, new byte[8]), "Artist", $"Track {id}",
        "", 300, 124, "8A", 5, "House", "Focused", false, SourceKind.MyLibrary, null, new() { CanReference = true });
    private static SetPlan Plan(SetRequirements requirements, params TrackSnapshot[] tracks) => new(requirements,
        tracks.Select((track, index) => new PlannedTrack(index + 1, track, false, 1, [])).ToArray(), []);

    [Fact]
    public void Middle_position_matches_hand_computed_scores_and_four_reasons()
    {
        var plan = Plan(Requirements(), Track(1), Track(2), Track(3) with { Bpm = 132, Energy = 7, MusicalKey = "9A" });
        var candidate = Track(10) with { Bpm = 128, Energy = 6, DurationSeconds = 270 };
        var result = Assert.Single(engine.RankAlternatives(plan, 2, [candidate], Now, 10));
        // Previous compatibility .877777..., next .832777...; target energy 5, duration fit .9.
        Assert.Equal(0.855, result.Transition);
        Assert.Equal(0.889, result.EnergyFit);
        Assert.Equal(0.9, result.DurationFit);
        Assert.Equal(0.872, result.Score);
        Assert.Equal(4, result.Reasons.Count);
        Assert.Equal("Previous BPM difference: 4. Next BPM difference: 4.", result.Reasons[0]);
        Assert.Equal("Previous: Compatible Camelot keys. Next: Compatible Camelot keys.", result.Reasons[1]);
        Assert.Equal("Energy 6/10; target 5 for GradualBuild.", result.Reasons[2]);
        Assert.Equal("Duration difference: -30 seconds.", result.Reasons[3]);
    }

    [Theory]
    [InlineData(1, 0.6)]
    [InlineData(2, 1.0)]
    [InlineData(3, 0.6)]
    public void First_middle_and_last_use_only_the_existing_neighbours(int position, double transition)
    {
        var plan = Plan(Requirements(), Track(1) with { Bpm = 124 }, Track(2) with { Bpm = 140 }, Track(3) with { Bpm = 124 });
        var result = Assert.Single(engine.RankAlternatives(plan, position, [Track(10)], Now, 1));
        Assert.Equal(transition, result.Transition);
    }

    [Fact]
    public void Single_track_has_transition_one_and_two_reasons()
    {
        var result = Assert.Single(engine.RankAlternatives(Plan(Requirements(300), Track(1)), 1, [Track(10)], Now, 10));
        Assert.Equal(1, result.Transition);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Equal("Energy 5/10; target 3 for GradualBuild.", result.Reasons[0]);
    }

    [Fact]
    public void Shuffling_does_not_change_ranking_ties_use_id_and_limit_is_respected()
    {
        var candidates = new[] { Track(12), Track(10), Track(11), Track(13) with { Energy = 9 } };
        var plan = Plan(Requirements(), Track(1), Track(2), Track(3));
        var expected = engine.RankAlternatives(plan, 2, candidates, Now, 2);
        var reversed = engine.RankAlternatives(plan, 2, candidates.Reverse(), Now, 2);
        var reorderedPlan = engine.RankAlternatives(plan with { Tracks = plan.Tracks.Reverse().ToArray() },
            2, candidates, Now, 2);
        Assert.Equal(new[] { Track(10).Id, Track(11).Id }, expected.Select(c => c.Track.Id));
        Assert.Equal(expected.Select(c => (c.Track.Id, c.Score)), reversed.Select(c => (c.Track.Id, c.Score)));
        Assert.Equal(expected.Select(c => (c.Track.Id, c.Score)), reorderedPlan.Select(c => (c.Track.Id, c.Score)));
        Assert.Equal(expected.SelectMany(c => c.Reasons), reversed.SelectMany(c => c.Reasons));
    }

    [Fact]
    public void Ordering_uses_unrounded_scores_even_when_reported_scores_tie()
    {
        var plan = Plan(Requirements(1000), Track(1) with { DurationSeconds = 1000 });
        var shorter = Track(10) with { DurationSeconds = 999, Energy = 3 };
        var exact = Track(11) with { DurationSeconds = 1000, Energy = 3 };
        var result = engine.RankAlternatives(plan, 1, [shorter, exact], Now, 10);
        Assert.Equal(new[] { exact.Id, shorter.Id }, result.Select(c => c.Track.Id));
        Assert.All(result, c => Assert.Equal(1, c.Score));
    }

    [Theory]
    [InlineData("rights")]
    [InlineData("territory")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("mix")]
    [InlineData("bpm")]
    [InlineData("unknown-bpm")]
    [InlineData("energy")]
    [InlineData("unknown-energy")]
    [InlineData("genre")]
    [InlineData("mood")]
    [InlineData("vocals")]
    [InlineData("excluded")]
    [InlineData("duration")]
    public void Every_eligibility_constraint_is_rechecked(string constraint)
    {
        var candidate = Track(10);
        var requirements = Requirements();
        candidate = constraint switch
        {
            "rights" => candidate with { Rights = new() },
            "territory" => candidate with { Rights = new() { CanReference = true, Territories = ["US"] } },
            "expired" => candidate with { Rights = new() { CanReference = true, LicenseEnd = Now } },
            "future" => candidate with { Rights = new() { CanReference = true, LicenseStart = Now.AddSeconds(1) } },
            "bpm" => candidate with { Bpm = 141 },
            "unknown-bpm" => candidate with { Bpm = null },
            "energy" => candidate with { Energy = 11 },
            "unknown-energy" => candidate with { Energy = null },
            "genre" => candidate with { Genre = "Techno" },
            "mood" => candidate with { Mood = "Calm" },
            "vocals" => candidate with { HasVocals = true },
            "duration" => candidate with { DurationSeconds = 0 },
            _ => candidate
        };
        if (constraint == "mix") requirements = requirements with { Mode = SetMode.Mixable };
        if (constraint == "excluded") requirements = requirements with { ExcludedTrackIds = [candidate.Id] };
        Assert.Empty(engine.RankAlternatives(Plan(requirements, Track(1)), 1, [candidate], Now, 10));
    }

    [Fact]
    public void Existing_tracks_and_longer_over_budget_swaps_are_excluded_but_shorter_swaps_are_allowed()
    {
        var current = Track(1);
        var neighbour = Track(2);
        var longer = Track(10) with { DurationSeconds = 361 };
        var atBudget = Track(11) with { DurationSeconds = 360 };
        var shorter = Track(12) with { DurationSeconds = 270 };
        var plan = Plan(Requirements(600), current, neighbour);
        Assert.Equal(new[] { atBudget.Id }, engine.RankAlternatives(plan, 1, [current, neighbour, longer, atBudget], Now, 10).Select(c => c.Track.Id));
        var overBudget = plan with { Requirements = Requirements(400) };
        Assert.Equal(new[] { shorter.Id },
            engine.RankAlternatives(overBudget, 1, [shorter, atBudget], Now, 10).Select(c => c.Track.Id));
        Assert.Single(engine.RankAlternatives(overBudget, 1, [Track(13)], Now, 10)); // Equal duration never worsens it.
    }

    [Fact]
    public void Duration_fit_is_clamped_at_zero_for_a_large_allowed_difference()
    {
        var result = Assert.Single(engine.RankAlternatives(Plan(Requirements(), Track(1)), 1,
            [Track(10) with { DurationSeconds = 601 }], Now, 10));
        Assert.Equal(0, result.DurationFit);
    }

    [Fact]
    public void Unknown_positions_and_locked_positions_fail_and_empty_candidates_succeed()
    {
        var plan = Plan(Requirements(300), Track(1));
        var missing = Assert.Throws<RuleViolation>(() => engine.RankAlternatives(plan, 2, [], Now, 10));
        Assert.Equal("not_found", missing.Code);
        Assert.Equal(404, missing.StatusCode);
        var locked = plan with { Tracks = [plan.Tracks[0] with { IsLocked = true }] };
        var error = Assert.Throws<RuleViolation>(() => engine.RankAlternatives(locked, 1, [], Now, 10));
        Assert.Equal("position_locked", error.Code);
        Assert.Equal(422, error.StatusCode);
        Assert.Empty(engine.RankAlternatives(plan, 1, [], Now, 10));
    }

    [Theory]
    [InlineData(EnergyCurve.GradualBuild, 9)]
    [InlineData(EnergyCurve.WarmUp, 5)]
    [InlineData(EnergyCurve.HighEnergy, 8.5)]
    [InlineData(EnergyCurve.Wave, 3)]
    [InlineData(EnergyCurve.PeakAndCooldown, 4)]
    public void Energy_target_reuses_each_curve_and_clamps_progress(EnergyCurve curve, double target)
    {
        var plan = Plan(Requirements(300) with { EnergyCurve = curve }, Track(1) with { DurationSeconds = 600 }, Track(2));
        var result = Assert.Single(engine.RankAlternatives(plan, 2, [Track(10)], Now, 10));
        Assert.Equal(Math.Round(1 - Math.Abs(5 - target) / 9, 3), result.EnergyFit);
    }

    [Fact]
    public void Reasons_are_culture_independent_and_tolerate_unknown_neighbour_metadata()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");
            var plan = Plan(Requirements() with { EnergyCurve = EnergyCurve.HighEnergy },
                Track(1) with { Bpm = null, MusicalKey = null }, Track(2), Track(3));
            var result = Assert.Single(engine.RankAlternatives(plan, 2, [Track(10) with { Bpm = 124.5m }], Now, 10));
            Assert.Equal("Previous BPM difference: unknown. Next BPM difference: 0.5.", result.Reasons[0]);
            Assert.Contains("Key compatibility is limited or unknown.", result.Reasons[1]);
            Assert.Contains("target 8.5", result.Reasons[2]);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
