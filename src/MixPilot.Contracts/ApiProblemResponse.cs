namespace MixPilot.Contracts;

/// <summary>Problem Details wire contract, including MixPilot's stable machine-readable code.</summary>
public sealed record ApiProblemResponse(string Type, string Title, int Status, string? Detail,
    string? Instance, string Code, string TraceId, IReadOnlyDictionary<string, string[]>? Errors);
