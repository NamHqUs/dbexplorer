using System.Net;
using System.Net.Http.Json;
using DbExplorer.Application.Contracts;
using DbExplorer.Infrastructure.Queries;
using Xunit;

namespace DbExplorer.Api.Tests;

// Pure guard tests. They need no database.
public sealed class ReadOnlyGuardTests
{
    [Theory]
    [InlineData("SELECT [DELETE] FROM dbo.Customers -- DELETE me\nWHERE Name = N'INSERT'")]
    [InlineData("WITH c AS (SELECT 1 AS x) SELECT * FROM c")]
    [InlineData("/* DELETE */ SELECT 1")]
    [InlineData("/* outer /* nested DELETE */ still comment */ SELECT 1")]
    [InlineData("DECLARE @n int = 1; SELECT @n")]
    public void Read_only_statements_are_allowed(string sql) =>
        Assert.Null(ReadOnlyGuard.FindViolation(sql));

    [Theory]
    [InlineData("SELECT 1; DELETE FROM dbo.Customers", "DELETE")]
    [InlineData("WITH c AS (SELECT 1 AS x) DELETE FROM dbo.Customers", "DELETE")]
    [InlineData("SELECT * INTO #copy FROM dbo.Customers", "INTO")]
    [InlineData("EXEC dbo.usp_GetOrders 1", "EXEC")]
    [InlineData("UPDATE dbo.Customers SET Name = N'x'", "UPDATE")]
    public void Writes_ddl_and_procedure_calls_are_rejected(string sql, string keyword) =>
        Assert.Equal(keyword, ReadOnlyGuard.FindViolation(sql));
}

[Collection(LiveSqlCollection.LiveSql)]
public sealed class QueryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [SkippableFact]
    public async Task Query_returns_every_result_set_with_column_metadata()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "query-multi", readOnly: true);

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/query", new
        {
            sql = "SELECT 1 AS A, 2 AS A; SELECT N'x' AS B",
        });
        var result = await response.Content.ReadFromJsonAsync<QueryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, result!.ResultSets.Count);
        Assert.Equal(["A", "A"], result.ResultSets[0].Columns.Select(c => c.Name));
        Assert.Equal("x", result.ResultSets[1].Rows[0][0]!.ToString());
    }

    [SkippableFact]
    public async Task Read_only_connection_rejects_write_statements()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "query-readonly", readOnly: true);

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/query", new { sql = "DELETE FROM dbo.Customers" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Sql_server_errors_are_returned_as_bad_request()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "query-error", readOnly: false);

        var response = await client.PostAsJsonAsync($"/api/connections/{id}/query", new { sql = "SELECT * FROM dbo.NoSuchTable" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Saved_queries_are_private_to_their_owner_and_can_be_updated_and_deleted()
    {
        var owner = await TestClients.AuthenticatedAsync(factory, "saved-owner");
        var other = await TestClients.AuthenticatedAsync(factory, "saved-other");

        var created = await owner.PostAsJsonAsync("/api/saved-queries", new { name = "Top orders", folder = "Reports", sqlText = "SELECT TOP 10 * FROM dbo.Orders", connectionId = (Guid?)null });
        var dto = await created.Content.ReadFromJsonAsync<SavedQueryDto>();

        var updated = await owner.PutAsJsonAsync($"/api/saved-queries/{dto!.Id}", new { name = "Top orders v2", folder = (string?)null, sqlText = "SELECT 1", connectionId = (Guid?)null });
        var foreignRead = await other.GetAsync($"/api/saved-queries/{dto.Id}");
        var list = await owner.GetFromJsonAsync<List<SavedQueryDto>>("/api/saved-queries");
        var deleted = await owner.DeleteAsync($"/api/saved-queries/{dto.Id}");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Contains(list!, q => q.Name == "Top orders v2");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
