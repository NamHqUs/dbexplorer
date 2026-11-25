using DbExplorer.Application.Domain;

namespace DbExplorer.Application.Contracts;

public sealed record ConnectionDto(
    Guid Id,
    string Name,
    string Host,
    int? Port,
    string? Database,
    DbAuthMode AuthMode,
    string? UserName,
    bool HasPassword,
    bool ReadOnly,
    bool TrustServerCertificate,
    int TimeoutSec,
    DateTimeOffset UpdatedAt);

public sealed record CreateConnectionRequest(
    string Name,
    string Host,
    int? Port,
    string? Database,
    DbAuthMode AuthMode,
    string? UserName,
    string? Password,
    bool ReadOnly,
    bool TrustServerCertificate,
    int TimeoutSec = 30);

// A null Password means "keep the stored password".
public sealed record UpdateConnectionRequest(
    string Name,
    string Host,
    int? Port,
    string? Database,
    DbAuthMode AuthMode,
    string? UserName,
    string? Password,
    bool ReadOnly,
    bool TrustServerCertificate,
    int TimeoutSec = 30);

public sealed record TestConnectionResult(bool Success, string? ServerVersion, string? Error, long ElapsedMs);
