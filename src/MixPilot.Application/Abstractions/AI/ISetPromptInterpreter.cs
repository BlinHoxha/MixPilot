using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.AI;

public interface ISetPromptInterpreter
{
    Task<SetIntentResponse> Interpret(SetIntentRequest request, CancellationToken ct);
}

