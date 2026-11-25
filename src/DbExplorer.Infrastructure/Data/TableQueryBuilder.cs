using System.Data;
using System.Globalization;
using System.Text.Json;
using DbExplorer.Application.Contracts;

namespace DbExplorer.Infrastructure.Data;

public static class TableQueryBuilder
{
    private static readonly HashSet<string> NonOrderableTypes = ["text", "ntext", "image", "xml", "geography", "geometry"];

    public static RowsQuery Build(TableModel table, TableRowsRequest request)
    {
        if (request.StartRow < 0) throw new ArgumentException("StartRow must be zero or greater.");
        if (request.EndRow <= request.StartRow) throw new ArgumentException("EndRow must be greater than StartRow.");

        var parameters = new List<QueryParam>();
        var predicates = new List<string>();

        if (request.FilterModel is not null)
        {
            foreach (var (colId, filter) in request.FilterModel)
            {
                var column = SqlIdentifiers.FindColumn(table, colId);
                predicates.Add(FilterPredicate(column, filter, parameters));
            }
        }

        if (!string.IsNullOrEmpty(request.QuickFilter))
        {
            predicates.Add(QuickFilterPredicate(table, request, parameters));
        }

        var where = predicates.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", predicates);
        var take = Math.Min(request.EndRow - request.StartRow, SqlIdentifiers.MaxBlockSize);

        return new RowsQuery(
            SqlIdentifiers.Qualified(table),
            string.Join(", ", table.Columns.Select(c => SqlIdentifiers.Quote(c.Name))),
            where,
            parameters,
            OrderBy(table, request.SortModel),
            request.StartRow,
            take);
    }

    private static string OrderBy(TableModel table, IReadOnlyList<SortItem>? sortModel)
    {
        var orders = new List<string>();

        if (sortModel is not null)
        {
            foreach (var item in sortModel)
            {
                var column = SqlIdentifiers.FindColumn(table, item.ColId);
                var direction = item.Sort.ToLowerInvariant() switch
                {
                    "asc" => "ASC",
                    "desc" => "DESC",
                    _ => throw new ArgumentException($"Sort must be 'asc' or 'desc', got '{item.Sort}'."),
                };
                orders.Add($"{SqlIdentifiers.Quote(column.Name)} {direction}");
            }
        }

        // The PK keeps paging stable. Without a PK, fall back to every orderable column when no sort is given.
        foreach (var key in table.PrimaryKey)
        {
            if (!orders.Any(o => o.StartsWith(SqlIdentifiers.Quote(key) + " ", StringComparison.Ordinal)))
                orders.Add($"{SqlIdentifiers.Quote(key)} ASC");
        }

        if (orders.Count == 0)
        {
            orders.AddRange(table.Columns
                .Where(c => !NonOrderableTypes.Contains(SqlIdentifiers.BaseType(c)))
                .Select(c => $"{SqlIdentifiers.Quote(c.Name)} ASC"));
        }

        return orders.Count == 0 ? "(SELECT NULL)" : string.Join(", ", orders);
    }

    private static string FilterPredicate(ColumnModel column, ColumnFilter filter, List<QueryParam> ps) =>
        filter.FilterType.ToLowerInvariant() switch
        {
            "text" => TextPredicate(column, filter, ps),
            "number" => NumberPredicate(column, filter, ps),
            "date" => DatePredicate(column, filter, ps),
            "set" => SetPredicate(column, filter, ps),
            _ => throw new ArgumentException($"Unsupported filter type '{filter.FilterType}'."),
        };

    private static string TextPredicate(ColumnModel column, ColumnFilter filter, List<QueryParam> ps)
    {
        var raw = SqlIdentifiers.Quote(column.Name);
        var text = $"CAST({raw} AS nvarchar(max))";

        switch (filter.Type)
        {
            case "blank":
                return $"({raw} IS NULL OR {text} = N'')";
            case "notBlank":
                return $"({raw} IS NOT NULL AND {text} <> N'')";
        }

        var value = Scalar(filter.Filter) ?? string.Empty;
        return filter.Type switch
        {
            "contains" => Like(text, "%" + EscapeLike(value) + "%", ps),
            "notContains" => "NOT " + Like(text, "%" + EscapeLike(value) + "%", ps),
            "startsWith" => Like(text, EscapeLike(value) + "%", ps),
            "endsWith" => Like(text, "%" + EscapeLike(value), ps),
            "equals" => Compare(text, "=", value, ps),
            "notEqual" => Compare(text, "<>", value, ps),
            _ => throw new ArgumentException($"Unsupported text filter '{filter.Type}'."),
        };
    }

    private static string NumberPredicate(ColumnModel column, ColumnFilter filter, List<QueryParam> ps)
    {
        var raw = SqlIdentifiers.Quote(column.Name);

        switch (filter.Type)
        {
            case "blank":
                return $"{raw} IS NULL";
            case "notBlank":
                return $"{raw} IS NOT NULL";
        }

        var from = ParseDecimal(filter.Filter);
        string Bind(decimal v) => AddParam(ps, v, SqlDbType.Decimal);

        return filter.Type switch
        {
            "equals" => $"{raw} = {Bind(from)}",
            "notEqual" => $"{raw} <> {Bind(from)}",
            "lessThan" => $"{raw} < {Bind(from)}",
            "lessThanOrEqual" => $"{raw} <= {Bind(from)}",
            "greaterThan" => $"{raw} > {Bind(from)}",
            "greaterThanOrEqual" => $"{raw} >= {Bind(from)}",
            "inRange" => $"{raw} >= {Bind(from)} AND {raw} <= {Bind(ParseDecimal(filter.FilterTo))}",
            _ => throw new ArgumentException($"Unsupported number filter '{filter.Type}'."),
        };
    }

    // Date filters compare whole days: "equals" matches any time on the given day.
    private static string DatePredicate(ColumnModel column, ColumnFilter filter, List<QueryParam> ps)
    {
        var raw = SqlIdentifiers.Quote(column.Name);

        switch (filter.Type)
        {
            case "blank":
                return $"{raw} IS NULL";
            case "notBlank":
                return $"{raw} IS NOT NULL";
        }

        var day = ParseDate(filter.Filter);
        string Bind(DateTime v) => AddParam(ps, v, SqlDbType.DateTime2);

        return filter.Type switch
        {
            "equals" => $"({raw} >= {Bind(day)} AND {raw} < {Bind(day.AddDays(1))})",
            "notEqual" => $"({raw} < {Bind(day)} OR {raw} >= {Bind(day.AddDays(1))})",
            "lessThan" => $"{raw} < {Bind(day)}",
            "greaterThan" => $"{raw} >= {Bind(day.AddDays(1))}",
            "inRange" => $"({raw} >= {Bind(day)} AND {raw} < {Bind(ParseDate(filter.FilterTo).AddDays(1))})",
            _ => throw new ArgumentException($"Unsupported date filter '{filter.Type}'."),
        };
    }

    private static string SetPredicate(ColumnModel column, ColumnFilter filter, List<QueryParam> ps)
    {
        var values = filter.Values ?? [];
        if (values.Count == 0) return "1 = 0";

        var text = $"CAST({SqlIdentifiers.Quote(column.Name)} AS nvarchar(max))";
        var names = values.Select(v => AddParam(ps, Scalar(v) ?? string.Empty, SqlDbType.NVarChar)).ToList();
        return $"{text} IN ({string.Join(", ", names)})";
    }

    // Ctrl+F: contains-match across the given columns, or all non-binary columns when none are given.
    private static string QuickFilterPredicate(TableModel table, TableRowsRequest request, List<QueryParam> ps)
    {
        var columns = request.QuickFilterColumns is { Count: > 0 }
            ? request.QuickFilterColumns.Select(c => SqlIdentifiers.FindColumn(table, c)).ToList()
            : table.Columns.Where(c => !SqlIdentifiers.IsBinary(c)).ToList();

        if (columns.Count == 0) return "1 = 0";

        var pattern = "%" + EscapeLike(request.QuickFilter!) + "%";
        var name = AddParam(ps, pattern, SqlDbType.NVarChar);
        var parts = columns.Select(c => $"CAST({SqlIdentifiers.Quote(c.Name)} AS nvarchar(max)) LIKE {name} ESCAPE '\\'");
        return "(" + string.Join(" OR ", parts) + ")";
    }

    private static string Like(string expression, string pattern, List<QueryParam> ps) =>
        $"{expression} LIKE {AddParam(ps, pattern, SqlDbType.NVarChar)} ESCAPE '\\'";

    private static string Compare(string expression, string op, string value, List<QueryParam> ps) =>
        $"{expression} {op} {AddParam(ps, value, SqlDbType.NVarChar)}";

    // Escapes LIKE metacharacters so user text matches literally. Pairs with ESCAPE '\'.
    internal static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal);

    internal static string AddParam(List<QueryParam> ps, object? value, SqlDbType type)
    {
        var name = "@p" + ps.Count;
        ps.Add(new QueryParam(name, value, type));
        return name;
    }

    // AG Grid sends filter values as JSON strings or numbers. Returns the raw text for either.
    internal static string? Scalar(JsonElement? element)
    {
        if (element is not { } e) return null;
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static decimal ParseDecimal(JsonElement? element) =>
        decimal.TryParse(Scalar(element), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new ArgumentException("Number filter value is not a valid number.");

    private static DateTime ParseDate(JsonElement? element) =>
        DateTime.TryParse(Scalar(element), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date
            : throw new ArgumentException("Date filter value is not a valid date.");
}
