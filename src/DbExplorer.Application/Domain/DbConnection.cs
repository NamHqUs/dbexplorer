namespace DbExplorer.Application.Domain;

public enum DbAuthMode
{
    SqlAuth = 0,
    WindowsAuth = 1,
}

public sealed class DbConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string? Database { get; set; }
    public DbAuthMode AuthMode { get; set; } = DbAuthMode.SqlAuth;
    public string? UserName { get; set; }
    public string? EncryptedPassword { get; set; }
    public bool ReadOnly { get; set; }
    public bool TrustServerCertificate { get; set; }
    public int TimeoutSec { get; set; } = 30;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
