using System.ComponentModel.DataAnnotations;
using AIFramework.Contracts;

namespace MixPilot.Contracts;

public sealed class ExplainSetRequest
{
    [Required, StringLength(2000, MinimumLength = 2)]
    public string Question { get; init; } = "Explain the musical choices and transitions in this set.";
}

public sealed record SetExplanationResponse(Guid SetId, int Version, GroundedAnswerResponse Explanation);
