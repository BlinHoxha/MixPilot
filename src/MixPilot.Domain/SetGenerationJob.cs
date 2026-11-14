namespace MixPilot.Domain;

public sealed class SetGenerationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string? RequirementsJson { get; set; }
    public string? Prompt { get; set; }
    public string? Territory { get; set; }
    public string Status { get; set; } = "queued";
    public int Progress { get; set; }
    public string? ErrorCode { get; set; }
    public Guid? ResultSetId { get; set; }
    public bool CancelRequested { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CorrelationId { get; set; } = "";
}
