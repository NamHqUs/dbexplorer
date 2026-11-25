using System.Data;
using System.Text.Json;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;
using DbExplorer.Infrastructure.Persistence;
using DbExplorer.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace DbExplorer.Infrastructure.Data;

public sealed class TableDataService(
    IConnectionService connections,
    ISchemaService schemaService,
    AppDbContext db) : ITableDataService
{
    private const string NoRowMessage = "The row was not found or has changed since it was loaded.";
    private const int MaxAuditStatementLength = 4000;
    private const int MaxLookupRows = 500;
    private static readonly HashSet<string> NonGroupableTypes = ["text", "ntext", "image", "xml", "geography", "geometry"];

    public async Task<TableRowsResponse?> GetRowsAsync(string ownerUserId, Guid connectionId, string schema, string table, TableRowsRequest request, CancellationToken ct)
    {
        var model = await FindTableAsync(ownerUserId, connectionId, schema, table, ct);
        if (model is null) return null;

        var found = await connections.GetWithSecretAsync(ownerUserId, connectionId, ct);
        if (found is null) return null;

        var query = TableQueryBuilder.Build(model, request);
        await using var conn = await OpenAsync(found.Value.Connection, found.Value.Password, ct);

        var rows = await ReadPageAsync(conn, query, found.Value.Connection.TimeoutSec, ct);
        long? lastRow = request.IncludeCount
            ? await CountAsync(conn, query, found.Value.Connection.TimeoutSec, ct)
            : null;

        return new TableRowsResponse(rows, lastRow, model.PrimaryKey.Count > 0);
    }

    public async Task<ChangeBatchResult?> ApplyChangesAsync(string ownerUserId, Guid connectionId, string schema, string table, ChangeBatchRequest request, CancellationToken ct)
    {
        var model = await FindTableAsync(ownerUserId, connectionId, schema, table, ct);
        if (model is null) return null;

        var found = await connections.GetWithSecretAsync(ownerUserId, connectionId, ct);
        if (found is null) return null;

        var (connection, password) = found.Value;
        if (model.IsView) throw new ArgumentException("Views cannot be edited.");
        if (connection.ReadOnly) throw new ConnectionReadOnlyException();

        // Every statement is built before the first SQL runs, so validation errors never reach the server.
        var operations = BuildOperations(model, request);

        await using var conn = await OpenAsync(connection, password, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        var inserted = new List<IReadOnlyDictionary<string, object?>>();
        var updated = new List<IReadOnlyDictionary<string, object?>>();
        var deleted = 0;
        ChangeError? failure = null;

        foreach (var op in operations)
        {
            try
            {
                await using var cmd = CreateCommand(conn, op.Statement.Sql, op.Statement.Parameters, connection.TimeoutSec, tx);
                switch (op.Group)
                {
                    case "deletes":
                        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException(NoRowMessage);
                        deleted++;
                        break;
                    case "updates":
                        updated.Add(await ReadSingleRowAsync(cmd, ct) ?? throw new InvalidOperationException(NoRowMessage));
                        break;
                    case "inserts":
                        inserted.Add(await ReadSingleRowAsync(cmd, ct) ?? throw new InvalidOperationException("The insert did not return a row."));
                        break;
                }
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                failure = new ChangeError(op.Group, op.Index, ex.Message);
                break;
            }
        }

        if (failure is not null)
        {
            await tx.RollbackAsync(ct);
            throw new ChangeBatchException([failure]);
        }

        await tx.CommitAsync(ct);

        await WriteAuditAsync(ownerUserId, connectionId, model, operations, inserted.Count, updated.Count, deleted, ct);

        return new ChangeBatchResult(inserted, updated, deleted);
    }

    private static List<Operation> BuildOperations(TableModel model, ChangeBatchRequest request)
    {
        var operations = new List<Operation>();
        var index = 0;
        foreach (var d in request.Deletes ?? [])
            operations.Add(new Operation("deletes", index++, TableWriteBuilder.Delete(model, d.Keys, d.RowVersion)));

        index = 0;
        foreach (var u in request.Updates ?? [])
            operations.Add(new Operation("updates", index++, TableWriteBuilder.Update(model, u.Keys, u.Values, u.RowVersion)));

        index = 0;
        foreach (var i in request.Inserts ?? [])
            operations.Add(new Operation("inserts", index++, TableWriteBuilder.Insert(model, i.Values)));

        return operations;
    }

    public async Task<LookupResponse?> LookupAsync(string ownerUserId, Guid connectionId, string schema, string table, LookupRequest request, CancellationToken ct)
    {
        var database = await schemaService.GetAsync(ownerUserId, connectionId, refresh: false, ct);
        var model = database?.Tables.FirstOrDefault(t =>
            string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase));
        if (model is null) return null;

        var found = await connections.GetWithSecretAsync(ownerUserId, connectionId, ct);
        if (found is null) return null;

        var (connection, password) = found.Value;
        var column = SqlIdentifiers.FindColumn(model, request.Column);
        var top = Math.Clamp(request.Top, 1, MaxLookupRows);

        // A single-column foreign key on this column: show the referenced rows that match the cell value.
        var fk = model.ForeignKeys.FirstOrDefault(f => f.ParentColumns.Count == 1 &&
            string.Equals(f.ParentColumns[0], column.Name, StringComparison.OrdinalIgnoreCase));
        if (fk is not null)
        {
            var target = database!.Tables.Single(t => t.Schema == fk.ReferencedSchema && t.Name == fk.ReferencedTable);
            var filters = request.Value is { } value && value.ValueKind != JsonValueKind.Null
                ? new Dictionary<string, ColumnFilter>
                {
                    [fk.ReferencedColumns[0]] = new("text", "equals", value, null, null),
                }
                : null;
            var rowsRequest = new TableRowsRequest(0, top, null, filters, request.Search, null, IncludeCount: false);

            var rowsQuery = TableQueryBuilder.Build(target, rowsRequest);
            await using var conn = await OpenAsync(connection, password, ct);
            var rows = await ReadPageAsync(conn, rowsQuery, connection.TimeoutSec, ct);
            return new LookupResponse("foreignKey", target.Schema, target.Name, fk.ReferencedColumns[0], rows, null);
        }

        // Otherwise: distinct values of the column with counts.
        if (SqlIdentifiers.IsBinary(column) || NonGroupableTypes.Contains(SqlIdentifiers.BaseType(column)))
            throw new ArgumentException($"Column '{column.Name}' cannot be grouped for lookup.");

        var searchQuery = TableQueryBuilder.Build(model, new TableRowsRequest(0, 1, null, null, request.Search,
            QuickFilterColumns: [column.Name], IncludeCount: false));
        var col = SqlIdentifiers.Quote(column.Name);
        var sql = $"SELECT TOP (@top) {col} AS [Value], COUNT_BIG(*) AS [Count] FROM {searchQuery.QualifiedTable} {searchQuery.Where} GROUP BY {col} ORDER BY COUNT_BIG(*) DESC, {col}";
        var parameters = searchQuery.Parameters.Concat(new[] { new QueryParam("@top", top, SqlDbType.Int) });

        await using var distinctConn = await OpenAsync(connection, password, ct);
        await using var cmd = CreateCommand(distinctConn, sql, parameters, connection.TimeoutSec);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var values = new List<LookupValue>();
        while (await reader.ReadAsync(ct))
        {
            values.Add(new LookupValue(ToJsonValue(reader.GetValue(0)), reader.GetInt64(1)));
        }
        return new LookupResponse("distinct", null, null, null, null, values);
    }

    private async Task<TableModel?> FindTableAsync(string ownerUserId, Guid connectionId, string schema, string table, CancellationToken ct)
    {
        var database = await schemaService.GetAsync(ownerUserId, connectionId, refresh: false, ct);
        return database?.Tables.FirstOrDefault(t =>
            string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<SqlConnection> OpenAsync(DbExplorer.Application.Domain.DbConnection connection, string? password, CancellationToken ct)
    {
        var builder = SqlServerConnectionStringFactory.Build(connection, password);
        var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    internal static SqlCommand CreateCommand(SqlConnection conn, string sql, IEnumerable<QueryParam> parameters, int timeoutSec, SqlTransaction? tx = null)
    {
        var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = timeoutSec };
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(new SqlParameter(p.Name, p.DbType) { Value = p.Value });
        }
        return cmd;
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> ReadPageAsync(SqlConnection conn, RowsQuery query, int timeoutSec, CancellationToken ct)
    {
        var sql = $"SELECT {query.SelectList} FROM {query.QualifiedTable} {query.Where} ORDER BY {query.OrderBy} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        var parameters = query.Parameters.Concat(new[]
        {
            new QueryParam("@skip", query.Skip, SqlDbType.Int),
            new QueryParam("@take", query.Take, SqlDbType.Int),
        });

        await using var cmd = CreateCommand(conn, sql, parameters, timeoutSec);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(ct)) rows.Add(ReadRow(reader));
        return rows;
    }

    private static async Task<long> CountAsync(SqlConnection conn, RowsQuery query, int timeoutSec, CancellationToken ct)
    {
        var sql = $"SELECT COUNT_BIG(*) FROM {query.QualifiedTable} {query.Where}";
        await using var cmd = CreateCommand(conn, sql, query.Parameters, timeoutSec);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task<IReadOnlyDictionary<string, object?>?> ReadSingleRowAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRow(reader) : null;
    }

    internal static IReadOnlyDictionary<string, object?> ReadRow(SqlDataReader reader)
    {
        var row = new Dictionary<string, object?>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = ToJsonValue(reader.GetValue(i));
        }
        return row;
    }

    // Provider-specific types (geography, hierarchyid, ...) are not JSON-serializable, so they are sent as text.
    internal static object? ToJsonValue(object raw) => raw switch
    {
        DBNull => null,
        string or bool or byte[] or DateTime or DateTimeOffset or Guid or decimal or TimeSpan => raw,
        _ when raw.GetType().IsPrimitive => raw,
        _ => raw.ToString(),
    };

    private async Task WriteAuditAsync(
        string ownerUserId, Guid connectionId, TableModel model, List<Operation> operations,
        int insertedCount, int updatedCount, int deletedCount, CancellationToken ct)
    {
        var statement = string.Join(";\n", operations.Select(o => o.Statement.Sql));
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = ownerUserId,
            ConnectionId = connectionId,
            Action = "table.changes",
            Target = $"{model.Schema}.{model.Name}",
            Statement = statement.Length > MaxAuditStatementLength ? statement[..MaxAuditStatementLength] : statement,
            Summary = $"inserted {insertedCount}, updated {updatedCount}, deleted {deletedCount}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private sealed record Operation(string Group, int Index, WriteStatement Statement);
}
