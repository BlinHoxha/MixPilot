using System.ComponentModel.DataAnnotations;

namespace MixPilot.Contracts;

public sealed class SetIntentRequest
{
    [Required, StringLength(1000, MinimumLength = 1), RegularExpression(@"(?s).*\S.*")]
    public string Prompt { get; init; } = "";

    [Required, RegularExpression("^[A-Z]{2}$")]
    public string Territory { get; init; } = "";
}

public sealed record SetIntentResponse(SetRequirementsRequest Requirements, string[] Assumptions,
    string[] Unresolved, string Provider, string? Model);
