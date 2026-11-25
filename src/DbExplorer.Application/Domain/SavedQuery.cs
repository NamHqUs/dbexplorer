namespace DbExplorer.Application.Domain;

public sealed class SavedQuery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid? ConnectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Folder { get; set; }
    public string SqlText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
