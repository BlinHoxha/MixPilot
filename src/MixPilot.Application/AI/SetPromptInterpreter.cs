using System.Text.Json;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using MixPilot.Application.Abstractions.AI;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.AI;

public sealed class SetPromptInterpreter(ITrackRepository tracks, IStructuredGenerationModel? model,
    AiFeatureOptions options) : ISetIntentService
{
    public async Task<SetIntentResponse> Interpret(SetIntentRequest request, CancellationToken ct)
    {
        if (!options.Enabled || model is null)
            throw new RuleViolation("prompt_interpretation_unavailable", "Prompt interpretation is not configured.", 503);

        var vocabulary = await tracks.GetVocabulary(ct);
        var instructions = $"Interpret the prompt as the closed JSON schema below. Use only the supplied genre and mood vocabulary. " +
            $"Energy curves: gradualBuild, wave, peakAndCooldown, highEnergy, warmUp. " +
            $"Genres: {JsonSerializer.Serialize(vocabulary.Genres)}. " +
            $"Moods: {JsonSerializer.Serialize(vocabulary.Moods)}. " +
            $"Schema: {SetIntentSchema.Json}";
        StructuredGenerationResult result;
        try
        {
            result = await model.GenerateStructuredAsync(new("set_intent", instructions, request.Prompt,
                SetIntentSchema.Value, 512), ct);
        }
        catch (HttpRequestException)
        { throw new RuleViolation("ai_provider_unavailable", "The AI provider is unavailable.", 503); }
        catch (ArgumentException)
        { throw new RuleViolation("ai_provider_unavailable", "The AI provider is unavailable.", 503); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new RuleViolation("ai_provider_timeout", "The AI provider timed out.", 503); }
        catch (StructuredGenerationException)
        { throw new RuleViolation("prompt_not_interpretable", "The prompt could not be interpreted."); }

        if (result.Status != StructuredGenerationStatus.Completed || result.OutputJson is null)
            throw new RuleViolation("prompt_not_interpretable", "The prompt could not be interpreted.");
        try
        {
            using var output = JsonDocument.Parse(result.OutputJson);
            return SetIntentMapping.Map(output.RootElement, vocabulary, request.Territory, result.Provider, result.Model);
        }
        catch (JsonException)
        { throw new RuleViolation("prompt_not_interpretable", "The prompt could not be interpreted."); }
    }
}
