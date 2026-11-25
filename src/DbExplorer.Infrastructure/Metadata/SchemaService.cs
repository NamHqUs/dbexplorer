using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DbExplorer.Infrastructure.Metadata;

public sealed class SchemaCacheOptions
{
    public const string SectionName = "Schema";

    public int CacheMinutes { get; set; } = 10;
}

public sealed class SchemaService(
    IConnectionService connections,
    ISchemaLoader loader,
    IMemoryCache cache,
    IOptions<SchemaCacheOptions> options) : ISchemaService
{
    public async Task<DatabaseSchema?> GetAsync(string ownerUserId, Guid connectionId, bool refresh, CancellationToken ct)
    {
        var found = await connections.GetWithSecretAsync(ownerUserId, connectionId, ct);
        if (found is null) return null;

        var (connection, password) = found.Value;

        // UpdatedAt in the key means editing the connection (host, database, credentials) drops the old schema.
        var key = $"schema:{connection.Id:N}:{connection.UpdatedAt.UtcTicks}";
        if (refresh) cache.Remove(key);

        return await cache.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(options.Value.CacheMinutes);
            return loader.LoadAsync(connection, password, ct);
        });
    }
}
