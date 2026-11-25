using System.Data;
using System.Text.Json;
using DbExplorer.Application.Contracts;

namespace DbExplorer.Infrastructure.Data;

public sealed record WriteStatement(string Sql, IReadOnlyList<QueryParam> Parameters);

public static class TableWriteBuilder
{
    public static WriteStatement Insert(TableModel table, IReadOnlyDictionary<string, JsonElement>? values)
    {
        var ps = new List<QueryParam>();
        var entries = (values ?? new Dictionary<string, JsonElement>())
            .Select(kv => (Column: SqlIdentifiers.FindColumn(table, kv.Key), Value: kv.Value))
            .ToList();

        foreach (var entry in entries) RequireWritable(entry.Column);

        var target = SqlIdentifiers.Qualified(table);
        if (entries.Count == 0)
            return new WriteStatement($"INSERT INTO {target} OUTPUT inserted.* DEFAULT VALUES", ps);

        var columns = string.Join(", ", entries.Select(e => SqlIdentifiers.Quote(e.Column.Name)));
        var placeholders = entries.Select(e => AddValue(ps, e.Column, e.Value)).ToList();
        return new WriteStatement($"INSERT INTO {target} ({columns}) OUTPUT inserted.* VALUES ({string.Join(", ", placeholders)})", ps);
    }

    public static WriteStatement Update(
        TableModel table,
        IReadOnlyDictionary<string, JsonElement> keys,
        IReadOnlyDictionary<string, JsonElement>? values,
        string? rowVersion)
    {
        var ps = new List<QueryParam>();
        var assignments = (values ?? new Dictionary<string, JsonElement>())
            .Select(kv => (Column: SqlIdentifiers.FindColumn(table, kv.Key), Value: kv.Value))
            .ToList();

        if (assignments.Count == 0) throw new ArgumentException("Update has no values to write.");
        foreach (var a in assignments) RequireWritable(a.Column);

        var sets = string.Join(", ", assignments.Select(a =>
            $"{SqlIdentifiers.Quote(a.Column.Name)} = {AddValue(ps, a.Column, a.Value)}"));
        var where = KeyPredicate(table, keys, ps) + RowVersionPredicate(table, rowVersion, ps);

        return new WriteStatement(
            $"UPDATE {SqlIdentifiers.Qualified(table)} SET {sets} OUTPUT inserted.* WHERE {where}",
            ps);
    }

    public static WriteStatement Delete(TableModel table, IReadOnlyDictionary<string, JsonElement> keys, string? rowVersion)
    {
        var ps = new List<QueryParam>();
        var where = KeyPredicate(table, keys, ps) + RowVersionPredicate(table, rowVersion, ps);
        return new WriteStatement($"DELETE FROM {SqlIdentifiers.Qualified(table)} WHERE {where}", ps);
    }

    // Keys must name exactly the primary key columns, so a row is always addressed by its identity.
    private static string KeyPredicate(TableModel table, IReadOnlyDictionary<string, JsonElement> keys, List<QueryParam> ps)
    {
        if (table.PrimaryKey.Count == 0)
            throw new ArgumentException("Table has no primary key. Updates and deletes are disabled for it.");

        var provided = keys.Keys.Select(k => SqlIdentifiers.FindColumn(table, k).Name).OrderBy(n => n).ToList();
        var expected = table.PrimaryKey.OrderBy(n => n).ToList();
        if (!provided.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Keys must contain exactly the primary key columns: " + string.Join(", ", table.PrimaryKey) + ".");

        var parts = table.PrimaryKey.Select(pk =>
        {
            var column = SqlIdentifiers.FindColumn(table, pk);
            var value = keys.First(kv => string.Equals(kv.Key, pk, StringComparison.OrdinalIgnoreCase)).Value;
            return $"{SqlIdentifiers.Quote(column.Name)} = {AddValue(ps, column, value)}";
        });
        return string.Join(" AND ", parts);
    }

    // Optimistic concurrency: applies only when the table has a rowversion column and the client sent a version.
    private static string RowVersionPredicate(TableModel table, string? rowVersion, List<QueryParam> ps)
    {
        var rowVersionColumn = table.Columns.FirstOrDefault(c => c.IsRowVersion);
        if (rowVersionColumn is null || rowVersion is null) return string.Empty;

        var bytes = TryFromBase64(rowVersion) ?? throw new ArgumentException("RowVersion must be base64 text.");
        return $" AND {SqlIdentifiers.Quote(rowVersionColumn.Name)} = {TableQueryBuilder.AddParam(ps, bytes, SqlDbType.VarBinary)}";
    }

    private static void RequireWritable(ColumnModel column)
    {
        if (column.IsIdentity) throw new ArgumentException($"Column '{column.Name}' is an identity column and cannot be written.");
        if (column.IsComputed) throw new ArgumentException($"Column '{column.Name}' is computed and cannot be written.");
        if (column.IsRowVersion) throw new ArgumentException($"Column '{column.Name}' is a rowversion column and cannot be written.");
    }

    private static string AddValue(List<QueryParam> ps, ColumnModel column, JsonElement value)
    {
        var type = SqlIdentifiers.ToSqlDbType(column);
        object? bound = value.ValueKind switch
        {
            JsonValueKind.Null => DBNull.Value,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String when SqlIdentifiers.IsBinary(column) =>
                TryFromBase64(value.GetString()!) ?? throw new ArgumentException($"Column '{column.Name}' expects base64 binary data."),
            JsonValueKind.String => value.GetString(),
            _ => throw new ArgumentException($"Unsupported value for column '{column.Name}'."),
        };
        return TableQueryBuilder.AddParam(ps, bound, type);
    }

    private static byte[]? TryFromBase64(string text)
    {
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
