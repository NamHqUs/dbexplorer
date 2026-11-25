using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;
using Microsoft.EntityFrameworkCore;

namespace DbExplorer.Infrastructure.Persistence;

public sealed class ConnectionService(AppDbContext db, ISecretProtector protector) : IConnectionService
{
    public async Task<IReadOnlyList<ConnectionDto>> ListAsync(string ownerUserId, CancellationToken ct)
    {
        var items = await db.DbConnections
            .AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<ConnectionDto?> GetAsync(string ownerUserId, Guid id, CancellationToken ct)
    {
        var entity = await FindOwnedAsync(ownerUserId, id, tracking: false, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<ConnectionDto> CreateAsync(string ownerUserId, CreateConnectionRequest request, CancellationToken ct)
    {
        ValidateCommon(request.Name, request.Host, request.Port, request.TimeoutSec, request.AuthMode, request.UserName);
        if (request.AuthMode == DbAuthMode.SqlAuth && string.IsNullOrEmpty(request.Password))
            throw new ArgumentException("Password is required for SQL authentication.");

        var entity = new DbConnection
        {
            OwnerUserId = ownerUserId,
            Name = request.Name.Trim(),
            Host = request.Host.Trim(),
            Port = request.Port,
            Database = NullIfBlank(request.Database),
            AuthMode = request.AuthMode,
            UserName = request.AuthMode == DbAuthMode.SqlAuth ? NullIfBlank(request.UserName) : null,
            EncryptedPassword = request.AuthMode == DbAuthMode.SqlAuth ? protector.Protect(request.Password!) : null,
            ReadOnly = request.ReadOnly,
            TrustServerCertificate = request.TrustServerCertificate,
            TimeoutSec = request.TimeoutSec,
        };

        db.DbConnections.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<ConnectionDto?> UpdateAsync(string ownerUserId, Guid id, UpdateConnectionRequest request, CancellationToken ct)
    {
        var entity = await FindOwnedAsync(ownerUserId, id, tracking: true, ct);
        if (entity is null) return null;

        ValidateCommon(request.Name, request.Host, request.Port, request.TimeoutSec, request.AuthMode, request.UserName);

        // A null password keeps the stored one. It is an error only when no stored password exists.
        var hasStoredPassword = entity.EncryptedPassword is not null;
        if (request.AuthMode == DbAuthMode.SqlAuth && string.IsNullOrEmpty(request.Password) && !hasStoredPassword)
            throw new ArgumentException("Password is required for SQL authentication.");

        entity.Name = request.Name.Trim();
        entity.Host = request.Host.Trim();
        entity.Port = request.Port;
        entity.Database = NullIfBlank(request.Database);
        entity.AuthMode = request.AuthMode;
        entity.UserName = request.AuthMode == DbAuthMode.SqlAuth ? NullIfBlank(request.UserName) : null;
        entity.ReadOnly = request.ReadOnly;
        entity.TrustServerCertificate = request.TrustServerCertificate;
        entity.TimeoutSec = request.TimeoutSec;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.AuthMode == DbAuthMode.WindowsAuth)
        {
            entity.EncryptedPassword = null;
        }
        else if (!string.IsNullOrEmpty(request.Password))
        {
            entity.EncryptedPassword = protector.Protect(request.Password);
        }

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(string ownerUserId, Guid id, CancellationToken ct)
    {
        var entity = await FindOwnedAsync(ownerUserId, id, tracking: true, ct);
        if (entity is null) return false;

        db.DbConnections.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(DbConnection Connection, string? Password)?> GetWithSecretAsync(string ownerUserId, Guid id, CancellationToken ct)
    {
        var entity = await FindOwnedAsync(ownerUserId, id, tracking: false, ct);
        if (entity is null) return null;

        var password = entity.EncryptedPassword is null ? null : protector.Unprotect(entity.EncryptedPassword);
        return (entity, password);
    }

    private Task<DbConnection?> FindOwnedAsync(string ownerUserId, Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.DbConnections : db.DbConnections.AsNoTracking();
        return query.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
    }

    private static void ValidateCommon(string name, string host, int? port, int timeoutSec, DbAuthMode authMode, string? userName)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
        if (name.Trim().Length > 200) throw new ArgumentException("Name must be 200 characters or fewer.");
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.");
        if (port is < 1 or > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
        if (timeoutSec is < 1 or > 600) throw new ArgumentException("TimeoutSec must be between 1 and 600.");
        if (authMode == DbAuthMode.SqlAuth && string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("UserName is required for SQL authentication.");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ConnectionDto ToDto(DbConnection c) => new(
        c.Id, c.Name, c.Host, c.Port, c.Database, c.AuthMode, c.UserName,
        HasPassword: c.EncryptedPassword is not null,
        c.ReadOnly, c.TrustServerCertificate, c.TimeoutSec, c.UpdatedAt);
}
