using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;
using DbExplorer.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace DbExplorer.Infrastructure.Metadata;

// Loads the whole schema with five catalog queries, regardless of table count.
// Rows are grouped by object id so each table is built with O(1) lookups.
public sealed class SqlServerSchemaLoader : ISchemaLoader
{
    private const string ObjectsSql = """
        SELECT o.object_id, s.name, o.name, o.type, OBJECT_DEFINITION(o.object_id)
        FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF') AND o.is_ms_shipped = 0
        """;

    private const string ColumnsSql = """
        SELECT c.object_id, c.column_id, c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale,
               c.is_nullable, c.is_identity, c.is_computed
        FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
        ORDER BY c.object_id, c.column_id
        """;

    private const string PrimaryKeysSql = """
        SELECT i.object_id, c.name, ic.key_ordinal
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.is_primary_key = 1
        ORDER BY i.object_id, ic.key_ordinal
        """;

    private const string ForeignKeysSql = """
        SELECT fk.object_id, fk.name, fk.parent_object_id, fk.referenced_object_id,
               pc.name, rc.name, fk.delete_referential_action_desc
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        ORDER BY fk.object_id, fkc.constraint_column_id
        """;

    private const string ParametersSql = """
        SELECT p.object_id, p.name, TYPE_NAME(p.user_type_id), p.max_length, p.precision, p.scale, p.is_output
        FROM sys.parameters p
        JOIN sys.objects o ON o.object_id = p.object_id
        WHERE o.type IN ('P', 'FN', 'IF', 'TF') AND o.is_ms_shipped = 0 AND p.parameter_id > 0
        ORDER BY p.object_id, p.parameter_id
        """;

    public async Task<DatabaseSchema> LoadAsync(DbConnection connection, string? password, CancellationToken ct)
    {
        var builder = SqlServerConnectionStringFactory.Build(connection, password);
        await using var sql = new SqlConnection(builder.ConnectionString);
        await sql.OpenAsync(ct);

        var objects = await QueryAsync(sql, ObjectsSql, r => new ObjectRow(
            r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3).Trim(), r.IsDBNull(4) ? null : r.GetString(4)), ct);
        var columns = Group(await QueryAsync(sql, ColumnsSql, r => new ColumnRow(
            r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt16(4), r.GetByte(5), r.GetByte(6),
            r.GetBoolean(7), r.GetBoolean(8), r.GetBoolean(9)), ct), c => c.ObjectId);
        var keys = Group(await QueryAsync(sql, PrimaryKeysSql, r => new KeyRow(r.GetInt32(0), r.GetString(1), r.GetByte(2)), ct), k => k.ObjectId);
        var fkRows = await QueryAsync(sql, ForeignKeysSql, r => new FkRow(
            r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetString(6)), ct);
        var parameters = Group(await QueryAsync(sql, ParametersSql, r => new ParamRow(
            r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt16(3), r.GetByte(4), r.GetByte(5), r.GetBoolean(6)), ct), p => p.ObjectId);

        var byId = objects.ToDictionary(o => o.ObjectId);
        var foreignKeys = BuildForeignKeys(fkRows, byId);

        // Lookups keep relation building linear in the number of foreign keys.
        var outgoingByParent = foreignKeys.ToLookup(f => f.ParentObjectId);
        var incomingByReferenced = foreignKeys.ToLookup(f => f.ReferencedObjectId);

        var tables = objects
            .Where(o => o.Type is "U" or "V")
            .Select(o => new TableModel(
                o.Schema,
                o.Name,
                o.Type == "V",
                o.Definition,
                columns.TryGetValue(o.ObjectId, out var cols) ? cols.Select(ToColumn).ToList() : [],
                keys.TryGetValue(o.ObjectId, out var pk) ? pk.Select(k => k.ColumnName).ToList() : [],
                outgoingByParent[o.ObjectId].Select(f => f.Model).ToList(),
                incomingByReferenced[o.ObjectId].Select(f => f.Model).ToList()))
            .OrderBy(t => t.Schema).ThenBy(t => t.Name)
            .ToList();

        var routines = objects
            .Where(o => o.Type is "P" or "FN" or "IF" or "TF")
            .Select(o => new RoutineModel(
                o.Schema,
                o.Name,
                KindOf(o.Type),
                o.Definition,
                parameters.TryGetValue(o.ObjectId, out var ps)
                    ? ps.Select(p => new ParameterModel(p.Name, FormatType(p.TypeName, p.MaxLength, p.Precision, p.Scale), p.IsOutput)).ToList()
                    : []))
            .OrderBy(r => r.Schema).ThenBy(r => r.Name)
            .ToList();

        return new DatabaseSchema(
            DateTimeOffset.UtcNow,
            tables,
            routines.Where(r => r.Kind == "Procedure").ToList(),
            routines.Where(r => r.Kind != "Procedure").ToList());
    }

    private static List<ForeignKeyRow> BuildForeignKeys(List<FkRow> rows, Dictionary<int, ObjectRow> byId) =>
        rows
            .GroupBy(r => r.FkObjectId)
            .Select(g =>
            {
                var first = g.First();
                var parent = byId[first.ParentObjectId];
                var referenced = byId[first.ReferencedObjectId];
                var model = new ForeignKeyModel(
                    first.FkName,
                    parent.Schema, parent.Name, g.Select(r => r.ParentColumn).ToList(),
                    referenced.Schema, referenced.Name, g.Select(r => r.ReferencedColumn).ToList(),
                    first.OnDelete);
                return new ForeignKeyRow(first.ParentObjectId, first.ReferencedObjectId, model);
            })
            .ToList();

    private static ColumnModel ToColumn(ColumnRow c) => new(
        c.Name,
        c.ColumnId,
        FormatType(c.TypeName, c.MaxLength, c.Precision, c.Scale),
        c.IsNullable,
        c.IsIdentity,
        c.IsComputed,
        c.TypeName == "timestamp");

    private static string KindOf(string type) => type switch
    {
        "P" => "Procedure",
        "FN" => "ScalarFunction",
        "TF" => "TableFunction",
        "IF" => "InlineTableFunction",
        _ => type,
    };

    // Renders a column type the way a developer would write it, e.g. nvarchar(50), decimal(10,2).
    internal static string FormatType(string typeName, short maxLength, byte precision, byte scale) => typeName switch
    {
        "nvarchar" or "nchar" when maxLength == -1 => $"{typeName}(max)",
        "nvarchar" or "nchar" => $"{typeName}({maxLength / 2})",
        "varchar" or "char" or "varbinary" or "binary" when maxLength == -1 => $"{typeName}(max)",
        "varchar" or "char" or "varbinary" or "binary" => $"{typeName}({maxLength})",
        "decimal" or "numeric" => $"{typeName}({precision},{scale})",
        "datetime2" or "datetimeoffset" or "time" => $"{typeName}({scale})",
        _ => typeName,
    };

    private static Dictionary<int, List<T>> Group<T>(List<T> rows, Func<T, int> key) =>
        rows.GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());

    private static async Task<List<T>> QueryAsync<T>(SqlConnection sql, string text, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(text, sql) { CommandTimeout = 120 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct)) rows.Add(map(reader));
        return rows;
    }

    private sealed record ObjectRow(int ObjectId, string Schema, string Name, string Type, string? Definition);
    private sealed record ColumnRow(int ObjectId, int ColumnId, string Name, string TypeName, short MaxLength, byte Precision, byte Scale, bool IsNullable, bool IsIdentity, bool IsComputed);
    private sealed record KeyRow(int ObjectId, string ColumnName, int KeyOrdinal);
    private sealed record FkRow(int FkObjectId, string FkName, int ParentObjectId, int ReferencedObjectId, string ParentColumn, string ReferencedColumn, string OnDelete);
    private sealed record ForeignKeyRow(int ParentObjectId, int ReferencedObjectId, ForeignKeyModel Model);
    private sealed record ParamRow(int ObjectId, string Name, string TypeName, short MaxLength, byte Precision, byte Scale, bool IsOutput);
}
