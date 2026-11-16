using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AIFramework.Core.Models;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.AI;

public static class SetIntentSchema
{
    public const string Json = """
        {
          "type": "object",
          "properties": {
            "durationMinutes": { "type": ["integer", "null"] },
            "minBpm": { "type": ["number", "null"] },
            "maxBpm": { "type": ["number", "null"] },
            "genre": { "type": ["string", "null"] },
            "mood": { "type": ["string", "null"] },
            "hasVocals": { "type": ["boolean", "null"] },
            "energyCurve": { "type": ["string", "null"], "enum": ["gradualBuild", "wave", "peakAndCooldown", "highEnergy", "warmUp", null] },
            "assumptions": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["durationMinutes", "minBpm", "maxBpm", "genre", "mood", "hasVocals", "energyCurve", "assumptions"],
          "additionalProperties": false
        }
        """;

    private static readonly JsonDocument Document = JsonDocument.Parse(Json);
    public static JsonElement Value => Document.RootElement;

    static SetIntentSchema() => StructuredJson.ValidateSchema(Value);
}

public static class SetIntentMapping
{
    public static SetIntentResponse Map(JsonElement output, TrackVocabulary vocabulary,
        string territory, string provider, string? model)
    {
        if (!StructuredJson.Matches(output, SetIntentSchema.Value))
            throw new RuleViolation("prompt_not_interpretable", "The prompt could not be interpreted.");

        var assumptions = output.GetProperty("assumptions").EnumerateArray()
            .Select(item => item.GetString()!.Trim()).Where(item => item.Length > 0)
            .Take(5).Select(item => item.Length <= 160 ? item : item[..160]).ToList();
        var unresolved = new List<string>();

        int duration = 3600;
        var minutes = output.GetProperty("durationMinutes");
        if (minutes.ValueKind == JsonValueKind.Number && minutes.TryGetInt32(out var count) && count is >= 1 and <= 480)
            duration = count * 60;
        else assumptions.Add("Duration defaulted to 60 minutes because it was missing or outside the supported range.");

        decimal Bpm(string name, decimal fallback)
        {
            var value = output.GetProperty(name);
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var bpm) && bpm is >= 20 and <= 300)
                return bpm;
            assumptions.Add($"{name} defaulted to {fallback} because it was missing or outside the supported range.");
            return fallback;
        }
        var minBpm = Bpm("minBpm", 110);
        var maxBpm = Bpm("maxBpm", 140);
        if (minBpm > maxBpm)
        {
            minBpm = 110;
            maxBpm = 140;
            assumptions.Add("BPM range defaulted to 110-140 because the minimum exceeded the maximum.");
        }

        string? MatchVocabulary(string name, IReadOnlyList<string> values)
        {
            var value = output.GetProperty(name);
            if (value.ValueKind == JsonValueKind.Null) return null;
            var proposed = value.GetString()!.Trim();
            var match = values.FirstOrDefault(item => string.Equals(item, proposed, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            unresolved.Add($"{name} '{proposed[..Math.Min(proposed.Length, 100)]}' is not in the owner's library vocabulary.");
            return null;
        }

        var vocal = output.GetProperty("hasVocals");
        var curve = output.GetProperty("energyCurve");
        var requirements = new SetRequirementsRequest
        {
            DurationSeconds = duration,
            MinBpm = minBpm,
            MaxBpm = maxBpm,
            Genre = MatchVocabulary("genre", vocabulary.Genres),
            Mood = MatchVocabulary("mood", vocabulary.Moods),
            HasVocals = vocal.ValueKind == JsonValueKind.Null ? null : vocal.GetBoolean(),
            EnergyCurve = curve.ValueKind == JsonValueKind.Null ? EnergyCurveType.GradualBuild
                : Enum.Parse<EnergyCurveType>(curve.GetString()!, ignoreCase: true),
            Mode = PlanningMode.Planning,
            Territory = territory,
            ExcludedTrackIds = []
        };
        if (curve.ValueKind == JsonValueKind.Null)
            assumptions.Add("Energy curve defaulted to gradualBuild because it was unspecified.");
        if (!Validator.TryValidateObject(requirements, new ValidationContext(requirements), new List<ValidationResult>(), true))
            throw new RuleViolation("prompt_not_interpretable", "The prompt could not be mapped to valid requirements.");
        return new(requirements, assumptions.ToArray(), unresolved.ToArray(), provider, model);
    }
}
