using System.Net;
using System.Net.Http.Json;
using DbExplorer.Application.Contracts;
using Xunit;

namespace DbExplorer.Api.Tests;

[Collection(LiveSqlCollection.LiveSql)]
public sealed class SchemaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [SkippableFact]
    public async Task Schema_describes_tables_keys_relations_views_and_routines()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "schema-shape", readOnly: true);

        var schema = await client.GetFromJsonAsync<DatabaseSchema>($"/api/connections/{id}/schema");

        var customers = schema!.Tables.Single(t => t.Name == "Customers");
        var orders = schema.Tables.Single(t => t.Name == "Orders");
        var view = schema.Tables.Single(t => t.Name == "vw_OrderTotals");
        var nameCol = customers.Columns.Single(c => c.Name == "Name");
        var idCol = customers.Columns.Single(c => c.Name == "Id");
        var rowVer = customers.Columns.Single(c => c.Name == "RowVer");

        Assert.Equal(["Id"], customers.PrimaryKey);
        Assert.Equal("nvarchar(50)", nameCol.SqlType);
        Assert.False(nameCol.IsNullable);
        Assert.True(idCol.IsIdentity);
        Assert.True(rowVer.IsRowVersion);
        Assert.Equal("decimal(10,2)", orders.Columns.Single(c => c.Name == "Total").SqlType);
        Assert.True(view.IsView);

        var fk = Assert.Single(orders.ForeignKeys);
        Assert.Equal("Customers", fk.ReferencedTable);
        Assert.Equal(["CustomerId"], fk.ParentColumns);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Contains(customers.ReferencedBy, r => r.ParentTable == "Orders");

        var proc = Assert.Single(schema.Procedures, p => p.Name == "usp_GetOrders");
        Assert.Equal("@CustomerId", proc.Parameters.Single().Name);
        Assert.Equal("int", proc.Parameters.Single().SqlType);
        Assert.Equal("ScalarFunction", schema.Functions.Single(f => f.Name == "fn_Double").Kind);
        Assert.Equal("InlineTableFunction", schema.Functions.Single(f => f.Name == "tvf_Orders").Kind);
    }

    [SkippableFact]
    public async Task Schema_is_served_from_cache_until_refresh_is_requested()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (client, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "schema-cache", readOnly: true);
        var url = $"/api/connections/{id}/schema";

        var first = await client.GetFromJsonAsync<DatabaseSchema>(url);
        var second = await client.GetFromJsonAsync<DatabaseSchema>(url);
        var refreshed = await client.GetFromJsonAsync<DatabaseSchema>(url + "?refresh=true");

        Assert.Equal(first!.LoadedAt, second!.LoadedAt);
        Assert.True(refreshed!.LoadedAt > first.LoadedAt);
    }

    [SkippableFact]
    public async Task Schema_of_another_users_connection_is_not_found()
    {
        Skip.IfNot(SqlTestDatabase.ServerConnection is not null, "DBEXPLORER_TEST_SQL is not set.");
        await SqlTestDatabase.EnsureCreatedAsync();
        var (_, id) = await SqlTestDatabase.CreateConnectionAsync(factory, "schema-owner", readOnly: true);
        var intruder = await TestClients.AuthenticatedAsync(factory, "schema-intruder");

        var response = await intruder.GetAsync($"/api/connections/{id}/schema");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
