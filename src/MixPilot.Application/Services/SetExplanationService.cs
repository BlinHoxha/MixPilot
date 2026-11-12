using System.Security.Cryptography;
using System.Text;
using AIFramework.Application.Services;
using AIFramework.Contracts;
using MixPilot.Application.Abstractions.Identity;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Application.AI;
using MixPilot.Contracts;
using MixPilot.Domain;

namespace MixPilot.Application.Services;

public sealed class SetExplanationService(ISetService sets, ICurrentUser user, SetExplanationEvidence evidence,
    KnowledgeAnswerService answers, AiFeatureOptions options) : ISetExplanationService
{
    public async Task<SetExplanationResponse> Explain(Guid id, ExplainSetRequest request, CancellationToken ct)
    {
        // Authorize through the same owner-scoped service before exposing data to an AI provider.
        var set = await sets.Get(id, null, ct);
        if (!options.Enabled) throw new RuleViolation("ai_unavailable", "Set explanations are not enabled.", 503);
        var tenantId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(user.OwnerKey)).AsSpan(0, 16));
        evidence.Prepare(set, tenantId);
        try
        {
            var answer = await answers.AnswerAsync(new AskKnowledgeRequest
            {
                Question = request.Question, Profile = SetDocumentProfileCatalog.ProfileName,
                TenantId = tenantId, DocumentIds = [set.Id], MaxResults = 20
            }, ct);
            return new(set.Id, set.Version, answer);
        }
        catch (HttpRequestException)
        { throw new RuleViolation("ai_provider_unavailable", "The AI provider is unavailable. The saved set is unchanged.", 503); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new RuleViolation("ai_provider_timeout", "The AI provider timed out. The saved set is unchanged.", 503); }
    }
}
