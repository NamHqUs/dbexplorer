using System.Text.Json;
using System.Net.Http.Json;
using DbExplorer.Application.Contracts;
using DbExplorer.Infrastructure.Relations;
using Xunit;

namespace DbExplorer.Api.Tests;

// Pure cascade-script tests. They need no database.
public sealed class CascadeScriptBuilderTests
{
    [Fact]
    public void Delete_script_removes_children_before_parents()
    {
        var customers = Table("Customers", pk: ["Id"], columns: [Col("Id", "int", identity: true)], referencedBy: [Fk("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers", ["Id"])]);
        var orders = Table("Orders", pk: ["Id"], columns: [Col("Id", "int", identity: true), Col("CustomerId", "int")], foreignKeys: [Fk("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers", ["Id"])]);
        var graph = Graph(
            [Node("dbo.Customers|1", "Customers", new() { ["Id"] = 1 }), Node("dbo.Orders|10", "Orders", new() { ["Id"] = 10, ["CustomerId"] = 1 })],
            [new RelationEdge("dbo.Orders|10", "dbo.Customers|1", "FK_Orders_Customers")]);

        var result = CascadeScriptBuilder.Build(graph, Tables(customers, orders), "delete");

        Assert.Equal(["DELETE FROM [dbo].[Orders] WHERE [Id] = 10;", "DELETE FROM [dbo].[Customers] WHERE [Id] = 1;"], result.Statements);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Mutual_nullable_references_are_broken_with_an_update_before_deleting()
    {
        var a = Table("A", pk: ["Id"], columns: [Col("Id", "int"), Col("BId", "int", nullable: true)], foreignKeys: [Fk("FK_A_B", "A", ["BId"], "B", ["Id"])], referencedBy: [Fk("FK_B_A", "B", ["AId"], "A", ["Id"])]);
        var b = Table("B", pk: ["Id"], columns: [Col("Id", "int"), Col("AId", "int", nullable: true)], foreignKeys: [Fk("FK_B_A", "B", ["AId"], "A", ["Id"])], referencedBy: [Fk("FK_A_B", "A", ["BId"], "B", ["Id"])]);
        var graph = Graph(
            [Node("dbo.A|1", "A", new() { ["Id"] = 1, ["BId"] = 2 }), Node("dbo.B|2", "B", new() { ["Id"] = 2, ["AId"] = 1 })],
            [new RelationEdge("dbo.A|1", "dbo.B|2", "FK_A_B"), new RelationEdge("dbo.B|2", "dbo.A|1", "FK_B_A")]);

        var result = CascadeScriptBuilder.Build(graph, Tables(a, b), "delete");

        Assert.Contains(result.Statements, s => s.StartsWith("UPDATE [dbo].[A] SET [BId] = NULL", StringComparison.Ordinal));
        Assert.Equal(2, result.Statements.Count(s => s.StartsWith("DELETE FROM", StringComparison.Ordinal)));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Cycle_with_non_nullable_keys_is_reported_and_left_out()
    {
        var a = Table("A", pk: ["Id"], columns: [Col("Id", "int"), Col("BId", "int")], foreignKeys: [Fk("FK_A_B", "A", ["BId"], "B", ["Id"])], referencedBy: [Fk("FK_B_A", "B", ["AId"], "A", ["Id"])]);
        var b = Table("B", pk: ["Id"], columns: [Col("Id", "int"), Col("AId", "int")], foreignKeys: [Fk("FK_B_A", "B", ["AId"], "A", ["Id"])], referencedBy: [Fk("FK_A_B", "A", ["BId"], "B", ["Id"])]);
        var graph = Graph(
            [Node("dbo.A|1", "A", new() { ["Id"] = 1, ["BId"] = 2 }), Node("dbo.B|2", "B", new() { ["Id"] = 2, ["AId"] = 1 })],
            [new RelationEdge("dbo.A|1", "dbo.B|2", "FK_A_B"), new RelationEdge("dbo.B|2", "dbo.A|1", "FK_B_A")]);

        var result = CascadeScriptBuilder.Build(graph, Tables(a, b), "delete");

        Assert.Empty(result.Statements);
        Assert.Contains(result.Warnings, w => w.Contains("Circular references", StringComparison.Ordinal));
    }

    [Fact]
    public void String_values_are_quoted_as_unicode_literals_with_embedded_quotes_doubled()
    {
        var customers = Table("Customers", pk: ["Id"], columns: [Col("Id", "int"), Col("Name", "nvarchar(50)")]);
        var graph = Graph([Node("dbo.Customers|1", "Customers", new() { ["Id"] = 1, ["Name"] = "O'Brien" })], []);

        var result = CascadeScriptBuilder.Build(graph, Tables(customers), "insert");

        Assert.Equal("INSERT INTO [dbo].[Customers] ([Id], [Name]) VALUES (1, N'O''Brien');", Assert.Single(result.Statements));
    }

    [Fact]
    public void Insert_script_creates_parents_first_and_enables_identity_insert()
    {
        var customers = Table("Customers", pk: ["Id"], columns: [Col("Id", "int", identity: true), Col("Name", "nvarchar(50)")], referencedBy: [Fk("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers", ["Id"])]);
        var orders = Table("Orders", pk: ["Id"], columns: [Col("Id", "int", identity: true), Col("CustomerId", "int")], foreignKeys: [Fk("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers", ["Id"])]);
        var graph = Graph(
            [Node("dbo.Orders|10", "Orders", new() { ["Id"] = 10, ["CustomerId"] = 1 }), Node("dbo.Customers|1", "Customers", new() { ["Id"] = 1, ["Name"] = "A" })],
            [new RelationEdge("dbo.Orders|10", "dbo.Customers|1", "FK_Orders_Customers")]);

        var result = CascadeScriptBuilder.Build(graph, Tables(customers, orders), "insert");

        Assert.Equal("SET IDENTITY_INSERT [dbo].[Customers] ON;", result.Statements[0]);
        Assert.Equal("SET IDENTITY_INSERT [dbo].[Orders] ON;", result.Statements[1]);
        Assert.StartsWith("INSERT INTO [dbo].[Customers]", result.Statements[2], StringComparison.Ordinal);
        Assert.StartsWith("INSERT INTO [dbo].[Orders]", result.Statements[3], StringComparison.Ordinal);
    }

    private static Dictionary<string, TableModel> Tables(params TableModel[] tables) =>
        tables.ToDictionary(t => $"{t.Schema}.{t.Name}", StringComparer.OrdinalIgnoreCase);

    private static RelationGraph Graph(IReadOnlyList<RelationNode> nodes, IReadOnlyList<RelationEdge> edges) =>
        new(nodes, edges, []);

    private static RelationNode Node(string id, string table, Dictionary<string, object?> row) =>
        new(id, "dbo", table, 0, row);

    private static ColumnModel Col(string name, string sqlType, bool identity = false, bool nullable = false) =>
        new(name, 0, sqlType, nullable, identity, false, false);

    private static ForeignKeyModel Fk(string name, string holder, string[] holderColumns, string referenced, string[] referencedColumns) =>
        new(name, "dbo", holder, holderColumns, "dbo", referenced, referencedColumns, "NO_ACTION");

    private static TableModel Table(
        string name,
        string[] pk,
        ColumnModel[] columns,
        ForeignKeyModel[]? foreignKeys = null,
        ForeignKeyModel[]? referencedBy = null) =>
        new("dbo", name, false, null, columns, pk, foreignKeys ?? [], referencedBy ?? []);
}

// Relation graph and script generation against the live fixture database.
[Collection(LiveSqlCollection.LiveSql)]
public sealed class RelationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [SkippableFact]
    public async Task Graph_follows_child_rows_and_cascade_script_deletes_them_first()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        await SqlTestDatabase.ExecuteAsync("DELETE FROM dbo.Orders; DELETE FROM dbo.Customers; INSERT INTO dbo.Customers (Name) VALUES (N'Hank'); INSERT INTO dbo.Orders (CustomerId, Total) SELECT Id, 1 FROM dbo.Customers; INSERT INTO dbo.Orders (CustomerId, Total) SELECT Id, 2 FROM dbo.Customers");
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "relations-live", readOnly: true);
        var hank = await FindCustomerIdAsync(client, id, "Hank");
        var request = new { schema = "dbo", table = "Customers", keys = new[] { new { Id = hank } }, maxDepth = 2 };

        var graph = await (await client.PostAsJsonAsync($"/api/connections/{id}/relations", request)).Content.ReadFromJsonAsync<GraphDto>();
        var script = await (await client.PostAsJsonAsync($"/api/connections/{id}/scripts/cascade", new { relation = request, mode = "delete" })).Content.ReadFromJsonAsync<ScriptDto>();

        Assert.Equal(3, graph!.Nodes.Count);
        Assert.Equal(1, graph.Nodes.Count(n => n.Table == "Customers"));
        Assert.Equal(2, graph.Nodes.Count(n => n.Table == "Orders"));
        Assert.Equal(2, graph.Edges.Count);
        var customerDelete = script!.Statements.FindIndex(s => s.StartsWith("DELETE FROM [dbo].[Customers]", StringComparison.Ordinal));
        var orderDeletes = script.Statements.Select((s, i) => (s, i)).Where(x => x.s.StartsWith("DELETE FROM [dbo].[Orders]", StringComparison.Ordinal)).Select(x => x.i).ToList();
        Assert.Equal(2, orderDeletes.Count);
        Assert.All(orderDeletes, i => Assert.True(i < customerDelete));
    }

    private static async Task<int> FindCustomerIdAsync(HttpClient client, Guid id, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Customers/rows", new
        {
            startRow = 0,
            endRow = 10,
            filterModel = new Dictionary<string, object> { ["Name"] = new { filterType = "text", type = "equals", filter = name } },
            includeCount = false,
        });
        var page = await response.Content.ReadFromJsonAsync<RowsDto>();
        return page!.Rows.Single()["Id"].GetInt32();
    }

    private sealed record RowsDto(List<Dictionary<string, JsonElement>> Rows, long? LastRow, bool HasPrimaryKey);

    private sealed record GraphDto(List<NodeDto> Nodes, List<EdgeDto> Edges, List<string> Warnings);

    private sealed record NodeDto(string Id, string Schema, string Table, int Depth);

    private sealed record EdgeDto(string ChildId, string ParentId, string ForeignKey);

    private sealed record ScriptDto(List<string> Statements, List<string> Warnings, bool Truncated);
}
