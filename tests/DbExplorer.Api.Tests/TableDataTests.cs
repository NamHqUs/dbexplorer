using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DbExplorer.Api.Tests;

[Collection(LiveSqlCollection.LiveSql)]
public sealed class TableDataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string EmptyRowVersion = "AAAAAAAAAAA=";

    [SkippableFact]
    public async Task Rows_are_filtered_sorted_and_counted_on_the_server()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        await SqlTestDatabase.ExecuteAsync("DELETE FROM dbo.Orders; DELETE FROM dbo.Customers; INSERT INTO dbo.Customers (Name) VALUES (N'Alice'), (N'Bob'), (N'Carol')");
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "rows-filter", readOnly: true);

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Customers/rows", new
        {
            startRow = 0,
            endRow = 100,
            sortModel = new[] { new { colId = "Name", sort = "desc" } },
            filterModel = new Dictionary<string, object>
            {
                ["Name"] = new { filterType = "text", type = "contains", filter = "o" },
            },
            includeCount = true,
        });
        var page = await response.Content.ReadFromJsonAsync<RowsDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, page!.LastRow);
        Assert.Equal("Carol", page.Rows[0]["Name"].GetString());
        Assert.Equal("Bob", page.Rows[1]["Name"].GetString());
    }

    [SkippableFact]
    public async Task Batch_inserts_updates_and_deletes_using_row_version_checks()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "rows-edit", readOnly: false);
        var rowsUrl = $"/api/connections/{id}/tables/dbo/Customers/changes";

        var inserted = await client.PostAsJsonAsync(rowsUrl, new { inserts = new[] { new { values = new { Name = "Erin" } } } });
        var insertedRow = (await inserted.Content.ReadFromJsonAsync<ChangesDto>())!.Inserted[0];

        var updated = await client.PostAsJsonAsync(rowsUrl, new
        {
            updates = new[] { new { keys = new { Id = insertedRow["Id"] }, values = new { Name = "Erica" }, rowVersion = insertedRow["RowVer"].GetString() } },
        });
        var updatedRow = (await updated.Content.ReadFromJsonAsync<ChangesDto>())!.Updated[0];

        var deleted = await client.PostAsJsonAsync(rowsUrl, new
        {
            deletes = new[] { new { keys = new { Id = updatedRow["Id"] }, rowVersion = updatedRow["RowVer"].GetString() } },
        });
        var result = await deleted.Content.ReadFromJsonAsync<ChangesDto>();

        Assert.Equal(HttpStatusCode.OK, inserted.StatusCode);
        Assert.Equal("Erica", updatedRow["Name"].GetString());
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(1, result!.Deleted);
    }

    [SkippableFact]
    public async Task Failing_statement_rolls_back_earlier_successful_statements_in_the_batch()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        await SqlTestDatabase.ExecuteAsync("DELETE FROM dbo.Orders; DELETE FROM dbo.Customers; INSERT INTO dbo.Customers (Name) VALUES (N'Dana')");
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "rows-conflict", readOnly: false);
        var rowsUrl = $"/api/connections/{id}/tables/dbo/Customers/changes";

        var before = (await FindRowAsync(client, id, "Dana"))!;
        var response = await client.PostAsJsonAsync(rowsUrl, new
        {
            updates = new[] { new { keys = new { Id = before["Id"] }, values = new { Name = "Dana2" }, rowVersion = before["RowVer"].GetString() } },
            inserts = new[] { new { values = new { Name = (string?)null } } },
        });

        var after = await FindRowAsync(client, id, "Dana");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(after);
        Assert.Equal(before["RowVer"].GetString(), after["RowVer"].GetString());
    }

    [SkippableFact]
    public async Task Stale_row_version_is_reported_as_a_conflict()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        await SqlTestDatabase.ExecuteAsync("DELETE FROM dbo.Orders; DELETE FROM dbo.Customers; INSERT INTO dbo.Customers (Name) VALUES (N'Eve')");
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "rows-stale", readOnly: false);
        var current = (await FindRowAsync(client, id, "Eve"))!;

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Customers/changes", new
        {
            updates = new[] { new { keys = new { Id = current["Id"] }, values = new { Name = "Eve2" }, rowVersion = EmptyRowVersion } },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<Dictionary<string, JsonElement>?> FindRowAsync(HttpClient client, Guid id, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Customers/rows", new
        {
            startRow = 0,
            endRow = 100,
            filterModel = new Dictionary<string, object> { ["Name"] = new { filterType = "text", type = "equals", filter = name } },
            includeCount = false,
        });
        return (await response.Content.ReadFromJsonAsync<RowsDto>())!.Rows.SingleOrDefault();
    }

    [SkippableFact]
    public async Task Read_only_connection_rejects_changes_before_any_sql_runs()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "rows-readonly", readOnly: true);

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Customers/changes", new
        {
            inserts = new[] { new { values = new { Name = "Blocked" } } },
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Views_cannot_be_edited()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "rows-view", readOnly: false);

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/vw_OrderTotals/changes", new
        {
            inserts = new[] { new { values = new { CustomerId = 1 } } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task Lookup_on_a_foreign_key_column_returns_the_referenced_row()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        await SqlTestDatabase.ExecuteAsync("DELETE FROM dbo.Orders; DELETE FROM dbo.Customers; INSERT INTO dbo.Customers (Name) VALUES (N'Frank'); INSERT INTO dbo.Orders (CustomerId, Total) SELECT Id, 5 FROM dbo.Customers WHERE Name = N'Frank'");
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "lookup-fk", readOnly: true);
        var frank = await FindRowAsync(client, id, "Frank");
        var order = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Orders/rows", new { startRow = 0, endRow = 10, includeCount = false });
        var orderRow = (await order.Content.ReadFromJsonAsync<RowsDto>())!.Rows.Single();

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Orders/lookup", new
        {
            column = "CustomerId",
            value = frank!["Id"],
        });
        var lookup = await response.Content.ReadFromJsonAsync<LookupDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("foreignKey", lookup!.Mode);
        Assert.Equal("Customers", lookup.TargetTable);
        Assert.Equal("Frank", Assert.Single(lookup.Rows!)["Name"].GetString());
        Assert.Equal(frank["Id"].GetInt32(), orderRow["CustomerId"].GetInt32());
    }

    [SkippableFact]
    public async Task Lookup_on_a_plain_column_returns_distinct_values_with_counts()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        await SqlTestDatabase.ExecuteAsync("DELETE FROM dbo.Orders; DELETE FROM dbo.Customers; INSERT INTO dbo.Customers (Name) VALUES (N'Gus'); INSERT INTO dbo.Orders (CustomerId, Total) SELECT Id, 5 FROM dbo.Customers; INSERT INTO dbo.Orders (CustomerId, Total) SELECT Id, 5 FROM dbo.Customers; INSERT INTO dbo.Orders (CustomerId, Total) SELECT Id, 7 FROM dbo.Customers");
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "lookup-distinct", readOnly: true);

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/tables/dbo/Orders/lookup", new { column = "Total", top = 10 });
        var lookup = await response.Content.ReadFromJsonAsync<LookupDto>();

        Assert.Equal("distinct", lookup!.Mode);
        Assert.Equal(2, lookup.Values!.Count);
        Assert.Equal(2, lookup.Values[0].Count);
        Assert.Equal(5m, lookup.Values[0].Value.GetDecimal());
    }

    // Response shapes as the test client reads them. Values stay as JsonElement so every column type can be checked.
    private sealed record RowsDto(List<Dictionary<string, JsonElement>> Rows, long? LastRow, bool HasPrimaryKey);

    private sealed record ChangesDto(List<Dictionary<string, JsonElement>> Inserted, List<Dictionary<string, JsonElement>> Updated, int Deleted);

    private sealed record LookupDto(string Mode, string? TargetTable, List<Dictionary<string, JsonElement>>? Rows, List<LookupValueDto>? Values);

    private sealed record LookupValueDto(JsonElement Value, long Count);
}
