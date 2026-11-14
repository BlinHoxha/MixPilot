using System.ComponentModel.DataAnnotations;

namespace MixPilot.Contracts;

public sealed class CreateSetGenerationJobRequest : IValidatableObject
{
    [Required, StringLength(150, MinimumLength = 1)] public string Name { get; init; } = "";
    public SetRequirementsRequest? Requirements { get; init; }
    public string? Prompt { get; init; }
    public string? Territory { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        bool structured = Requirements is not null;
        bool prompted = Prompt is not null || Territory is not null;
        if (structured == prompted)
            yield return new("Supply either requirements or prompt with territory.", [nameof(Requirements), nameof(Prompt)]);
        if (prompted && (string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > 1000 ||
                         Territory is null || !System.Text.RegularExpressions.Regex.IsMatch(Territory, "^[A-Z]{2}$")))
            yield return new("Prompt must be 1-1000 nonblank characters and territory must be two uppercase letters.", [nameof(Prompt), nameof(Territory)]);
        if (structured && Requirements is not null &&
            !Validator.TryValidateObject(Requirements, new ValidationContext(Requirements), new List<ValidationResult>(), true))
            yield return new("Requirements are invalid.", [nameof(Requirements)]);
    }
}

public sealed record SetGenerationJobResponse(Guid Id, string Kind, string Status, int Progress,
    string? ErrorCode, Guid? ResultSetId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string CorrelationId);
