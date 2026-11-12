using MixPilot.Domain;

namespace MixPilot.Tests;

public sealed class DjEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static SetRequirements Requirements() => new(900, 120, 130, "House", null, null, EnergyCurve.GradualBuild, SetMode.Planning, "HU", []);
    private static TrackSnapshot Track(int id, int energy = 5) => new(new Guid(id, 0, 0, new byte[8]), "Artist", $"Track {id}", "", 300, 124, "8A", energy, "House", "", false, SourceKind.MyLibrary, null, new() { CanReference = true });

    [Theory]
    [InlineData("12A", "1A", 0.85)]
    [InlineData("8A", "8B", 0.85)]
    [InlineData("8A", "8A", 1)]
    [InlineData("8A", "2B", 0.15)]
    [InlineData(null, "8A", 0)]
    [InlineData("13A", "1A", 0)]
    public void Harmonic_rules_handle_wraparound_relative_keys_and_unknowns(string? a, string? b, double expected) =>
        Assert.Equal(expected, DjEngine.HarmonicScore(a, b));

    [Fact]
    public void Rights_require_permissions_and_current_territorial_entitlement()
    {
        var rights = new RightsProfile { CanReference = true, CanMix = true, Territories = ["HU"], LicenseStart = Now.AddDays(-1), LicenseEnd = Now.AddDays(1) };
        Assert.True(rights.Allows(SetMode.Mixable, "HU", Now));
        Assert.False(rights.Allows(SetMode.Planning, "US", Now));
        Assert.False(rights.Allows(SetMode.Planning, "HU", Now.AddDays(1)));
        Assert.False(rights.Allows(SetMode.Planning, "HU", Now.AddDays(-2)));
        Assert.False((rights with { CanMix = false }).Allows(SetMode.Mixable, "HU", Now));
        Assert.False(new RightsProfile().Allows(SetMode.Planning, "HU", Now));
    }

    [Fact]
    public void Golden_catalog_is_deterministic_and_builds_energy()
    {
        var catalog = Enumerable.Range(1, 8).Select(i => Track(i, i + 1)).ToArray();
        var engine = new DjEngine();
        var plan = engine.Generate(catalog, Requirements(), Now);
        var reversed = engine.Generate(catalog.Reverse(), Requirements(), Now);
        Assert.Equal(plan.Tracks.Select(t => t.Track.Id), reversed.Tracks.Select(t => t.Track.Id));
        Assert.Equal(new[] { Track(2).Id, Track(4).Id, Track(6).Id }, plan.Tracks.Select(t => t.Track.Id));
        Assert.Equal(900, plan.DurationSeconds); Assert.Equal(3, plan.Tracks.Select(t => t.Track.Id).Distinct().Count());
        Assert.True(plan.Tracks[^1].Track.Energy >= plan.Tracks[0].Track.Energy);
        Assert.All(plan.Tracks, t => Assert.InRange(t.Compatibility, 0, 1));
    }

    [Fact]
    public void Constraints_exclusions_unknown_metadata_and_duration_are_enforced()
    {
        var first = Track(1); var wrongGenre = Track(2) with { Genre = "Techno" };
        var unknown = Track(3) with { Bpm = null }; var excluded = Track(4);
        var plan = new DjEngine().Generate([first, wrongGenre, unknown, excluded], Requirements() with { ExcludedTrackIds = [excluded.Id] }, Now);
        Assert.Equal(first.Id, Assert.Single(plan.Tracks).Track.Id); Assert.NotEmpty(plan.Warnings);
        Assert.Throws<RuleViolation>(() => new DjEngine().Generate([first], Requirements() with { DurationSeconds = 60 }, Now));
    }

    [Fact]
    public void Locked_tracks_fail_closed_if_deleted_or_ineligible()
    {
        var engine = new DjEngine();
        var locked = new PlannedTrack(2, Track(1), true, 1, []);
        Assert.Equal("locked_track_unavailable", Assert.Throws<RuleViolation>(() => engine.Generate([Track(2)], Requirements(), Now, [locked])).Code);
        Assert.Equal("locked_position_unreachable", Assert.Throws<RuleViolation>(() => engine.Generate([Track(1)], Requirements(), Now, [locked])).Code);
    }
}
