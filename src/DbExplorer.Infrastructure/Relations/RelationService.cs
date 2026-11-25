using System.Data;
using System.Globalization;
using System.Text.Json;
using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;
using DbExplorer.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace DbExplorer.Infrastructure.Relations;

// Breadth-first walk over the foreign-key graph, one batched query per FK edge per level.
// Outgoing FKs of a table lead to parent rows; incoming FKs lead to child rows.
public sealed class RelationService(IConnectionService connections, ISchemaService schemaService) : IRelationService
{
    private const int MaxKeys = 500;
    private const int MaxDepthLimit = 5;
    private const int MaxRowsPerEdgeLimit = 5000;
    private const int TupleChunkSize = 500;

    public async Task<RelationGraph?> BuildGraphAsync(string ownerUserId, Guid connectionId, RelationRequest request, CancellationToken ct)
    {
        var context = await LoadContextAsync(ownerUserId, connectionId, ct);
        return context is null ? null : await BuildAsync(context, request, ct);
    }

    public async Task<CascadeScriptResponse?> GenerateScriptAsync(string ownerUserId, Guid connectionId, CascadeScriptRequest request, CancellationToken ct)
    {
        var context = await LoadContextAsync(ownerUserId, connectionId, ct);
        if (context is null) return null;

        var graph = await BuildAsync(context, request.Relation, ct);
        return CascadeScriptBuilder.Build(graph, context.Tables, request.Mode);
    }

    private sealed record Context(IReadOnlyDictionary<string, TableModel> Tables, DbConnection Connection, string? Password);

    private sealed record Match(RelationNode Source, IReadOnlyDictionary<string, object?> Row);

    private async Task<Context?> LoadContextAsync(string owner, Guid id, CancellationToken ct)
    {
        var database = await schemaService.GetAsync(owner, id, refresh: false, ct);
        if (database is null) return null;

        var found = await connections.GetWithSecretAsync(owner, id, ct);
        if (found is null) return null;

        var tables = database.Tables.ToDictionary(t => $"{t.Schema}.{t.Name}", StringComparer.OrdinalIgnoreCase);
        return new Context(tables, found.Value.Connection, found.Value.Password);
    }

    private static async Task<RelationGraph> BuildAsync(Context ctx, RelationRequest request, CancellationToken ct)
    {
        if (!ctx.Tables.TryGetValue($"{request.Schema}.{request.Table}", out var start))
            throw new ArgumentException($"Table {request.Schema}.{request.Table} was not found.");
        if (start.PrimaryKey.Count == 0)
            throw new ArgumentException($"{start.Schema}.{start.Name} has no primary key, so relations cannot start from it.");
        if (request.Keys.Count == 0) throw new ArgumentException("At least one key is required.");
        if (request.Keys.Count > MaxKeys) throw new ArgumentException($"At most {MaxKeys} keys can be selected at once.");
        foreach (var key in request.Keys) ValidateKeyColumns(start, key);

        var maxDepth = Math.Clamp(request.MaxDepth, 0, MaxDepthLimit);
        var limit = Math.Clamp(request.MaxRowsPerEdge, 1, MaxRowsPerEdgeLimit);
        var graph = new GraphBuilder();

        await using var conn = await TableDataService.OpenAsync(ctx.Connection, ctx.Password, ct);
        var timeout = ctx.Connection.TimeoutSec;

        var keyTuples = request.Keys.Select(k => start.PrimaryKey.Select(pk => JsonKeyValue(k, pk)).ToArray()).ToList();
        var startRows = await QueryByTuplesAsync(conn, start, start.PrimaryKey, keyTuples, keyTuples.Count, timeout, ct);
        if (startRows.Count < keyTuples.Count)
            graph.Warn($"{keyTuples.Count - startRows.Count} selected key(s) were not found in {start.Schema}.{start.Name}.");

        var frontier = new List<RelationNode>();
        foreach (var row in startRows)
        {
            if (graph.TryAdd(start, row, 0, out var node)) frontier.Add(node);
        }

        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<RelationNode>();
            foreach (var group in frontier.GroupBy(n => $"{n.Schema}.{n.Table}", StringComparer.OrdinalIgnoreCase))
            {
                var table = ctx.Tables[group.Key];
                var sources = group.ToList();

                foreach (var fk in table.ForeignKeys)
                {
                    var target = ctx.Tables[$"{fk.ReferencedSchema}.{fk.ReferencedTable}"];
                    var (matches, truncated) = await FetchRelatedAsync(conn, target, fk.ReferencedColumns, sources, fk.ParentColumns, limit, timeout, ct);
                    if (truncated) graph.Warn($"Rows reached through {fk.Name} were truncated at {limit} per relation.");

                    foreach (var match in matches)
                    {
                        if (graph.TryAdd(target, match.Row, depth + 1, out var parent)) next.Add(parent);
                        graph.AddEdge(match.Source.Id, parent.Id, fk.Name);
                    }
                }

                foreach (var fk in table.ReferencedBy)
                {
                    var child = ctx.Tables[$"{fk.ParentSchema}.{fk.ParentTable}"];
                    var (matches, truncated) = await FetchRelatedAsync(conn, child, fk.ParentColumns, sources, fk.ReferencedColumns, limit, timeout, ct);
                    if (truncated) graph.Warn($"Rows reached through {fk.Name} were truncated at {limit} per relation.");

                    foreach (var match in matches)
                    {
                        if (graph.TryAdd(child, match.Row, depth + 1, out var childNode)) next.Add(childNode);
                        graph.AddEdge(childNode.Id, match.Source.Id, fk.Name);
                    }
                }
            }
            frontier = next;
        }

        return graph.Build();
    }

    // Finds rows of target whose targetColumns equal the sourceColumns values of any source row.
    private static async Task<(List<Match> Matches, bool Truncated)> FetchRelatedAsync(
        SqlConnection conn,
        TableModel target,
        IReadOnlyList<string> targetColumns,
        IReadOnlyList<RelationNode> sources,
        IReadOnlyList<string> sourceColumns,
        int limit,
        int timeout,
        CancellationToken ct)
    {
        var sourcesByKey = new Dictionary<string, List<RelationNode>>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var values = ValuesOf(source.Row, sourceColumns);
            if (values is null) continue;
            var key = KeyOf(values);
            if (!sourcesByKey.TryGetValue(key, out var list)) sourcesByKey[key] = list = [];
            list.Add(source);
        }
        if (sourcesByKey.Count == 0) return ([], false);

        var tuples = sourcesByKey.Values.Select(list => ValuesOf(list[0].Row, sourceColumns)!).ToList();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        for (var i = 0; i < tuples.Count && rows.Count <= limit; i += TupleChunkSize)
        {
            var chunk = tuples.Skip(i).Take(TupleChunkSize).ToList();
            rows.AddRange(await QueryByTuplesAsync(conn, target, targetColumns, chunk, limit + 1, timeout, ct));
        }

        var truncated = rows.Count > limit;
        var matches = new List<Match>();
        foreach (var row in rows.Take(limit))
        {
            var values = ValuesOf(row, targetColumns);
            if (values is null || !sourcesByKey.TryGetValue(KeyOf(values), out var list)) continue;
            matches.AddRange(list.Select(source => new Match(source, row)));
        }
        return (matches, truncated);
    }

    // One statement per batch of key tuples: a single column uses IN, several columns use OR of AND groups.
    private static async Task<List<IReadOnlyDictionary<string, object?>>> QueryByTuplesAsync(
        SqlConnection conn,
        TableModel table,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> tuples,
        int top,
        int timeout,
        CancellationToken ct)
    {
        var parameters = new List<QueryParam>();
        var columnModels = columns.Select(c => SqlIdentifiers.FindColumn(table, c)).ToList();

        string predicate;
        if (columnModels.Count == 1)
        {
            var column = columnModels[0];
            var names = tuples.Select(t => TableQueryBuilder.AddParam(parameters, t[0], SqlIdentifiers.ToSqlDbType(column)));
            predicate = $"{SqlIdentifiers.Quote(column.Name)} IN ({string.Join(", ", names)})";
        }
        else
        {
            var clauses = tuples.Select(t =>
                "(" + string.Join(" AND ", columnModels.Select((c, i) =>
                    $"{SqlIdentifiers.Quote(c.Name)} = {TableQueryBuilder.AddParam(parameters, t[i], SqlIdentifiers.ToSqlDbType(c))}")) + ")");
            predicate = string.Join(" OR ", clauses);
        }

        var selectList = string.Join(", ", table.Columns.Select(c => SqlIdentifiers.Quote(c.Name)));
        var order = table.PrimaryKey.Count > 0
            ? string.Join(", ", table.PrimaryKey.Select(SqlIdentifiers.Quote))
            : "(SELECT NULL)";
        parameters.Add(new QueryParam("@top", top, SqlDbType.Int));

        var sql = $"SELECT TOP (@top) {selectList} FROM {SqlIdentifiers.Qualified(table)} WHERE {predicate} ORDER BY {order}";
        await using var cmd = TableDataService.CreateCommand(conn, sql, parameters, timeout);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(ct)) rows.Add(TableDataService.ReadRow(reader));
        return rows;
    }

    private static void ValidateKeyColumns(TableModel table, IReadOnlyDictionary<string, JsonElement> key)
    {
        var provided = key.Keys.Select(k => SqlIdentifiers.FindColumn(table, k).Name).OrderBy(n => n).ToList();
        var expected = table.PrimaryKey.OrderBy(n => n).ToList();
        if (!provided.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Each key must contain exactly the primary key columns: " + string.Join(", ", table.PrimaryKey) + ".");
    }

    // Client keys arrive as JSON. Numbers become decimal so they bind to any integer column.
    private static object? JsonKeyValue(IReadOnlyDictionary<string, JsonElement> key, string column)
    {
        var element = key.First(kv => string.Equals(kv.Key, column, StringComparison.OrdinalIgnoreCase)).Value;
        return element.ValueKind switch
        {
            JsonValueKind.Null => DBNull.Value,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => decimal.Parse(element.GetRawText(), CultureInfo.InvariantCulture),
            JsonValueKind.String => element.GetString(),
            _ => throw new ArgumentException($"Unsupported key value for column '{column}'."),
        };
    }

    // Returns null when any key column is NULL, because NULL never matches a relation.
    private static object?[]? ValuesOf(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> columns)
    {
        var values = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            if (!row.TryGetValue(columns[i], out var value) || value is null or DBNull) return null;
            values[i] = value;
        }
        return values;
    }

    private static string KeyOf(object?[] values) => string.Join("\u001f", values.Select(KeyPart));

    private static string KeyPart(object? value) => value switch
    {
        null or DBNull => "\u0000NULL",
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private sealed class GraphBuilder
    {
        private readonly Dictionary<string, RelationNode> _nodes = new(StringComparer.Ordinal);
        private readonly List<RelationNode> _order = [];
        private readonly HashSet<string> _edgeKeys = new(StringComparer.Ordinal);
        private readonly List<RelationEdge> _edges = [];
        private readonly List<string> _warnings = [];
        private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public bool TryAdd(TableModel table, IReadOnlyDictionary<string, object?> row, int depth, out RelationNode node)
        {
            var keyColumns = table.PrimaryKey.Count > 0 ? table.PrimaryKey : table.Columns.Select(c => c.Name).ToList();
            var id = $"{table.Schema}.{table.Name}|" + KeyOf(ValuesOf(row, keyColumns) ?? []);

            if (_nodes.TryGetValue(id, out node!)) return false;

            node = new RelationNode(id, table.Schema, table.Name, depth, row);
            _nodes[id] = node;
            _order.Add(node);
            if (table.PrimaryKey.Count == 0)
                Warn($"{table.Schema}.{table.Name} has no primary key; its related rows are listed but cannot be scripted.");
            return true;
        }

        public void AddEdge(string childId, string parentId, string foreignKey)
        {
            if (_edgeKeys.Add($"{childId}\u0001{parentId}\u0001{foreignKey}"))
                _edges.Add(new RelationEdge(childId, parentId, foreignKey));
        }

        public void Warn(string message)
        {
            if (_warned.Add(message)) _warnings.Add(message);
        }

        public RelationGraph Build() => new(_order, _edges, _warnings);
    }
}
