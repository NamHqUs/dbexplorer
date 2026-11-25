namespace DbExplorer.Application.Domain;

public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Statement { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
