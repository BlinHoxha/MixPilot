namespace MixPilot.Domain;

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerKey { get; set; } = "";
    public string Key { get; set; } = "";
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public string BodyHash { get; set; } = "";
    public string State { get; set; } = "pending";
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ContentType { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
