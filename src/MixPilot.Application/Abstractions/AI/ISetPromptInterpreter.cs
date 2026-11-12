using MixPilot.Contracts;

namespace MixPilot.Application.Abstractions.AI;

// Structured intent needs its own adapter; evidence-based Q&A is not a JSON intent parser.
public interface ISetPromptInterpreter
{
    Task<SetRequirementsRequest> Interpret(string prompt, CancellationToken ct);
}

