using System.Data;
using DbExplorer.Application.Contracts;

namespace DbExplorer.Infrastructure.Data;

// A parameter kept as data so the same predicate can be bound to several commands (page and count).
public sealed record QueryParam(string Name, object? Value, SqlDbType DbType);

public sealed record RowsQuery(
    string QualifiedTable,
    string SelectList,
    string Where,
    IReadOnlyList<QueryParam> Parameters,
    string OrderBy,
    int Skip,
    int Take);

public static class SqlIdentifiers
{
    public const int MaxBlockSize = 1000;

    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    public static string Qualified(TableModel table) => $"{Quote(table.Schema)}.{Quote(table.Name)}";

    // "nvarchar(50)" -> "nvarchar". Used to pick comparison and parameter rules.
    public static string BaseType(ColumnModel column) => column.SqlType.Split('(')[0];

    public static bool IsBinary(ColumnModel column) => BaseType(column) is "binary" or "varbinary" or "image" or "timestamp";

    // Column ids come from the client, so they are matched against the cached metadata only.
    public static ColumnModel FindColumn(TableModel table, string colId) =>
        table.Columns.FirstOrDefault(c => string.Equals(c.Name, colId, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown column '{colId}'.");

    public static SqlDbType ToSqlDbType(ColumnModel column) => BaseType(column).ToLowerInvariant() switch
    {
        "int" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "smallint" => SqlDbType.SmallInt,
        "tinyint" => SqlDbType.TinyInt,
        "bit" => SqlDbType.Bit,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "money" => SqlDbType.Money,
        "smallmoney" => SqlDbType.SmallMoney,
        "float" => SqlDbType.Float,
        "real" => SqlDbType.Real,
        "datetime" => SqlDbType.DateTime,
        "smalldatetime" => SqlDbType.SmallDateTime,
        "datetime2" => SqlDbType.DateTime2,
        "date" => SqlDbType.Date,
        "time" => SqlDbType.Time,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "binary" or "varbinary" or "timestamp" => SqlDbType.VarBinary,
        _ => SqlDbType.NVarChar,
    };
}
