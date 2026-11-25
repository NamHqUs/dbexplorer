using System.Diagnostics;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;
using DbExplorer.Infrastructure.Data;
using DbExplorer.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DbExplorer.Infrastructure.Queries;

public sealed class QueryService(IConnectionService connections, AppDbContext db) : IQueryService
{
    private const int DefaultMaxRows = 10_000;
    private const int MaxTimeoutSec = 600;
    private const int MaxAuditStatementLength = 4000;

    public async Task<QueryResponse?> ExecuteAsync(string ownerUserId, Guid connectionId, QueryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Sql)) throw new ArgumentException("SQL is required.");

        var found = await connections.GetWithSecretAsync(ownerUserId, connectionId, ct);
        if (found is null) return null;
        var (connection, password) = found.Value;

        if (connection.ReadOnly && ReadOnlyGuard.FindViolation(request.Sql) is { } keyword)
            throw new ConnectionReadOnlyException($"'{keyword}' is not allowed on a read-only connection.");

        var timeout = Math.Clamp(request.TimeoutSec ?? connection.TimeoutSec, 1, MaxTimeoutSec);
        var maxRows = Math.Clamp(request.MaxRows ?? DefaultMaxRows, 1, DefaultMaxRows);

        var clock = Stopwatch.StartNew();
        await using var conn = await TableDataService.OpenAsync(connection, password, ct);
        var (resultSets, rowsAffected) = await RunAsync(conn, request.Sql, timeout, maxRows, ct);
        clock.Stop();

        await WriteAuditAsync(ownerUserId, connectionId, request.Sql, resultSets.Count, rowsAffected, ct);

        return new QueryResponse(resultSets, rowsAffected, clock.ElapsedMilliseconds);
    }

    private static async Task<(List<ResultSet> ResultSets, int RowsAffected)> RunAsync(
        SqlConnection conn, string sql, int timeoutSec, int maxRows, CancellationToken ct)
    {
        var resultSets = new List<ResultSet>();
        try
        {
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = timeoutSec };
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            do
            {
                if (reader.FieldCount == 0) continue;

                var columns = Enumerable.Range(0, reader.FieldCount)
                    .Select(i => new ColumnInfo(reader.GetName(i), reader.GetDataTypeName(i)))
                    .ToList();

                var rows = new List<IReadOnlyList<object?>>();
                var truncated = false;
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count == maxRows)
                    {
                        truncated = true;
                        break;
                    }

                    var values = new object?[reader.FieldCount];
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = TableDataService.ToJsonValue(reader.GetValue(i));
                    }
                    rows.Add(values);
                }
                resultSets.Add(new ResultSet(columns, rows, truncated));
            } while (await reader.NextResultAsync(ct));

            return (resultSets, Math.Max(0, reader.RecordsAffected));
        }
        catch (SqlException ex)
        {
            throw new QueryExecutionException(ex.Message);
        }
    }

    private async Task WriteAuditAsync(string ownerUserId, Guid connectionId, string sql, int resultSetCount, int rowsAffected, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = ownerUserId,
            ConnectionId = connectionId,
            Action = "query.execute",
            Statement = sql.Length > MaxAuditStatementLength ? sql[..MaxAuditStatementLength] : sql,
            Summary = $"{resultSetCount} result set(s), {rowsAffected} row(s) affected",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SavedQueryService(AppDbContext db, IConnectionService connections) : ISavedQueryService
{
    public async Task<IReadOnlyList<SavedQueryDto>> ListAsync(string ownerUserId, Guid? connectionId, CancellationToken ct)
    {
        var query = db.SavedQueries.AsNoTracking().Where(q => q.OwnerUserId == ownerUserId);
        if (connectionId is { } filter) query = query.Where(q => q.ConnectionId == filter);

        var items = await query.OrderBy(q => q.Folder).ThenBy(q => q.Name).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<SavedQueryDto?> GetAsync(string ownerUserId, Guid id, CancellationToken ct)
    {
        var entity = await db.SavedQueries.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && q.OwnerUserId == ownerUserId, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<SavedQueryDto> CreateAsync(string ownerUserId, SavedQueryRequest request, CancellationToken ct)
    {
        Validate(request);
        await EnsureConnectionOwnedAsync(ownerUserId, request.ConnectionId, ct);

        var entity = new SavedQuery { OwnerUserId = ownerUserId };
        Apply(entity, request);
        db.SavedQueries.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<SavedQueryDto?> UpdateAsync(string ownerUserId, Guid id, SavedQueryRequest request, CancellationToken ct)
    {
        var entity = await db.SavedQueries.FirstOrDefaultAsync(q => q.Id == id && q.OwnerUserId == ownerUserId, ct);
        if (entity is null) return null;

        Validate(request);
        await EnsureConnectionOwnedAsync(ownerUserId, request.ConnectionId, ct);

        Apply(entity, request);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(string ownerUserId, Guid id, CancellationToken ct)
    {
        var entity = await db.SavedQueries.FirstOrDefaultAsync(q => q.Id == id && q.OwnerUserId == ownerUserId, ct);
        if (entity is null) return false;

        db.SavedQueries.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureConnectionOwnedAsync(string ownerUserId, Guid? connectionId, CancellationToken ct)
    {
        if (connectionId is null) return;
        if (await connections.GetAsync(ownerUserId, connectionId.Value, ct) is null)
            throw new ArgumentException("The connection was not found.");
    }

    private static void Validate(SavedQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name is required.");
        if (request.Name.Trim().Length > 200) throw new ArgumentException("Name must be 200 characters or fewer.");
        if (request.Folder is { Length: > 100 }) throw new ArgumentException("Folder must be 100 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.SqlText)) throw new ArgumentException("SQL text is required.");
        if (request.SqlText.Length > 100_000) throw new ArgumentException("SQL text must be 100,000 characters or fewer.");
    }

    private static void Apply(SavedQuery entity, SavedQueryRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Folder = string.IsNullOrWhiteSpace(request.Folder) ? null : request.Folder.Trim();
        entity.SqlText = request.SqlText;
        entity.ConnectionId = request.ConnectionId;
    }

    private static SavedQueryDto ToDto(SavedQuery q) =>
        new(q.Id, q.Name, q.Folder, q.SqlText, q.ConnectionId, q.UpdatedAt);
}
