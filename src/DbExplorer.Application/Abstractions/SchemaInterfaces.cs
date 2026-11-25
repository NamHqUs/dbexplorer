using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;

namespace DbExplorer.Application.Abstractions;

public interface ISchemaLoader
{
    Task<DatabaseSchema> LoadAsync(DbConnection connection, string? password, CancellationToken ct);
}

public interface ISchemaService
{
    // Returns null when the connection does not exist or is not owned by the user.
    Task<DatabaseSchema?> GetAsync(string ownerUserId, Guid connectionId, bool refresh, CancellationToken ct);
}
