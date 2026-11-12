using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using MixPilot.Domain;

namespace MixPilot.Infrastructure.AI;

internal sealed class UnavailableChatModel : IChatModel
{
    public Task<GroundedGenerationResult> GenerateAsync(GroundedGenerationRequest request, CancellationToken ct) =>
        throw new RuleViolation("ai_unavailable", "AI is disabled in this environment.", 503);
}
