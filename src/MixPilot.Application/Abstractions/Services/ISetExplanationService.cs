using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.Services;

public interface ISetExplanationService
{
    Task<SetExplanationResponse> Explain(Guid id, ExplainSetRequest request, CancellationToken ct);
}
