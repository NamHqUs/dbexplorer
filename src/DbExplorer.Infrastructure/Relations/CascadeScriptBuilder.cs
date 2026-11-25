using System.Globalization;
using DbExplorer.Application.Contracts;
using DbExplorer.Infrastructure.Data;

namespace DbExplorer.Infrastructure.Relations;

// Turns a relation graph into ordered T-SQL. Pure code: no database access.
//   delete: a row is deleted only after every row that references it is gone.
//   insert: a row is inserted only after every row it references exists.
// Cycles are broken with UPDATE ... SET fk = NULL when the FK is nullable (delete mode).
// Other cycles are reported as warnings and their rows are left out of the script.
public static class CascadeScriptBuilder
{
    public const int MaxStatements = 5000;

    public static CascadeScriptResponse Build(RelationGraph graph, IReadOnlyDictionary<string, TableModel> tables, string mode)
    {
        return mode switch
        {
            "delete" => BuildDelete(graph, tables),
            "insert" => BuildInsert(graph, tables),
            _ => throw new ArgumentException("Mode must be 'delete' or 'insert'."),
        };
    }

    private static CascadeScriptResponse BuildDelete(RelationGraph graph, IReadOnlyDictionary<string, TableModel> tables)
    {
        var warnings = graph.Warnings.ToList();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);

        // Rows without a primary key cannot be addressed for DELETE.
        foreach (var node in graph.Nodes.Where(n => TableOf(n, tables).PrimaryKey.Count == 0).OrderBy(n => n.Id))
        {
            warnings.Add($"{node.Schema}.{node.Table} has no primary key; its rows were left out of the delete script.");
            nodes.Remove(node.Id);
        }

        var edges = graph.Edges.Where(e => nodes.ContainsKey(e.ChildId) && nodes.ContainsKey(e.ParentId)).ToList();
        var incoming = nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (var e in edges) incoming[e.ParentId]++;

        var statements = new List<string>();
        var pending = new SortedSet<string>(nodes.Keys, StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var ready = pending.FirstOrDefault(id => incoming[id] == 0);
            if (ready is not null)
            {
                var node = nodes[ready];
                statements.Add(DeleteStatement(node, TableOf(node, tables)));
                pending.Remove(ready);
                foreach (var e in edges.Where(e => e.ChildId == ready).ToList())
                {
                    incoming[e.ParentId]--;
                    edges.Remove(e);
                }
                continue;
            }

            // Everything left is part of a cycle. Break one edge whose FK can be nulled.
            var breakable = edges.FirstOrDefault(e => pending.Contains(e.ChildId) && pending.Contains(e.ParentId)
                && IsNullableForeignKey(nodes[e.ChildId], e.ForeignKey, tables));
            if (breakable is null)
            {
                warnings.Add("Circular references that cannot be broken with nullable keys: " +
                    string.Join(", ", pending) + ". These rows were left out of the delete script.");
                break;
            }

            var child = nodes[breakable.ChildId];
            statements.Add(NullifyStatement(child, breakable.ForeignKey, TableOf(child, tables)));
            edges.Remove(breakable);
            incoming[breakable.ParentId]--;
        }

        return Finish(statements, warnings);
    }

    private static CascadeScriptResponse BuildInsert(RelationGraph graph, IReadOnlyDictionary<string, TableModel> tables)
    {
        var warnings = graph.Warnings.ToList();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        var edges = graph.Edges.ToList();

        var outgoing = nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (var e in edges) outgoing[e.ChildId]++;

        var statements = new List<string>();
        var identityTables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new SortedSet<string>(nodes.Keys, StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var ready = pending.FirstOrDefault(id => outgoing[id] == 0);
            if (ready is null)
            {
                warnings.Add("Circular references prevent a safe insert order: " +
                    string.Join(", ", pending) + ". These rows were left out of the insert script.");
                break;
            }

            var node = nodes[ready];
            var table = TableOf(node, tables);
            statements.Add(InsertStatement(node, table));
            if (table.Columns.Any(c => c.IsIdentity)) identityTables.Add(SqlIdentifiers.Qualified(table));

            pending.Remove(ready);
            foreach (var e in edges.Where(e => e.ParentId == ready).ToList())
            {
                outgoing[e.ChildId]--;
                edges.Remove(e);
            }
        }

        // IDENTITY_INSERT is per table, so it wraps the whole script for each identity table.
        var wrapped = identityTables.Select(t => $"SET IDENTITY_INSERT {t} ON;")
            .Concat(statements)
            .Concat(identityTables.Select(t => $"SET IDENTITY_INSERT {t} OFF;"))
            .ToList();

        return Finish(wrapped, warnings);
    }

    private static CascadeScriptResponse Finish(List<string> statements, List<string> warnings)
    {
        var truncated = statements.Count > MaxStatements;
        var kept = truncated ? statements.Take(MaxStatements).ToList() : statements;
        if (truncated)
            warnings.Add($"Script has {statements.Count} statements; only the first {MaxStatements} are included.");
        return new CascadeScriptResponse(kept, warnings, truncated);
    }

    private static string DeleteStatement(RelationNode node, TableModel table) =>
        $"DELETE FROM {SqlIdentifiers.Qualified(table)} WHERE {KeyPredicate(node, table)};";

    private static string NullifyStatement(RelationNode child, string foreignKey, TableModel table)
    {
        var fk = FindForeignKey(table, foreignKey);
        var sets = string.Join(", ", fk.ParentColumns.Select(c => $"{SqlIdentifiers.Quote(c)} = NULL"));
        return $"UPDATE {SqlIdentifiers.Qualified(table)} SET {sets} WHERE {KeyPredicate(child, table)};";
    }

    private static string InsertStatement(RelationNode node, TableModel table)
    {
        var columns = table.Columns.Where(c => !c.IsComputed && !c.IsRowVersion).ToList();
        var names = string.Join(", ", columns.Select(c => SqlIdentifiers.Quote(c.Name)));
        var values = string.Join(", ", columns.Select(c => SqlLiteral.From(node.Row.TryGetValue(c.Name, out var v) ? v : null)));
        return $"INSERT INTO {SqlIdentifiers.Qualified(table)} ({names}) VALUES ({values});";
    }

    // Keys come from the row values loaded earlier, so the statement addresses exactly that row.
    private static string KeyPredicate(RelationNode node, TableModel table) =>
        string.Join(" AND ", table.PrimaryKey.Select(pk =>
            $"{SqlIdentifiers.Quote(pk)} = {SqlLiteral.From(node.Row.TryGetValue(pk, out var v) ? v : null)}"));

    private static bool IsNullableForeignKey(RelationNode child, string foreignKey, IReadOnlyDictionary<string, TableModel> tables)
    {
        var table = TableOf(child, tables);
        var fk = FindForeignKey(table, foreignKey);
        return fk.ParentColumns.All(name => table.Columns.First(c => c.Name == name).IsNullable);
    }

    // ForeignKeys on a table are the constraints it holds, so ParentColumns are its own columns.
    private static ForeignKeyModel FindForeignKey(TableModel table, string name) =>
        table.ForeignKeys.First(f => f.Name == name);

    private static TableModel TableOf(RelationNode node, IReadOnlyDictionary<string, TableModel> tables) =>
        tables[$"{node.Schema}.{node.Table}"];
}

internal static class SqlLiteral
{
    // Renders a value read from the database as a T-SQL literal. Strings use N'' with quotes doubled.
    public static string From(object? value) => value switch
    {
        null or DBNull => "NULL",
        bool b => b ? "1" : "0",
        string s => "N'" + s.Replace("'", "''", StringComparison.Ordinal) + "'",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        DateTime dt => "'" + dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "'",
        DateTimeOffset dto => "'" + dto.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture) + "'",
        Guid g => "'" + g.ToString() + "'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => "N'" + value.ToString()!.Replace("'", "''", StringComparison.Ordinal) + "'",
    };
}
