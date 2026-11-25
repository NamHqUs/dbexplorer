using System.Net.Http.Json;
using DbExplorer.Api.Contracts;
using Microsoft.Data.SqlClient;

namespace DbExplorer.Api.Tests;

// Live-database tests share one fixture database and reset its rows, so they run one class at a time.
[CollectionDefinition(LiveSqlCollection.LiveSql)]
public sealed class LiveSqlCollection
{
    public const string LiveSql = "LiveSql";
}

// Live SQL Server fixtures. Requires DBEXPLORER_TEST_SQL, e.g.
// "Server=127.0.0.1,14330;User Id=sa;Password=...;TrustServerCertificate=True".
// Tests that need it are skipped when it is not set.
internal static class SqlTestDatabase
{
    public const string DatabaseName = "DbExplorerTests";

    public static readonly string? ServerConnection = Environment.GetEnvironmentVariable("DBEXPLORER_TEST_SQL");

    private static readonly string[] Statements =
    [
        "CREATE TABLE dbo.Customers (Id int IDENTITY(1,1) PRIMARY KEY, Name nvarchar(50) NOT NULL, RowVer rowversion)",
        "CREATE TABLE dbo.Orders (Id int IDENTITY(1,1) PRIMARY KEY, CustomerId int NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers(Id) ON DELETE CASCADE, Total decimal(10,2) NOT NULL)",
        "CREATE VIEW dbo.vw_OrderTotals AS SELECT CustomerId, SUM(Total) AS Total FROM dbo.Orders GROUP BY CustomerId",
        "CREATE PROCEDURE dbo.usp_GetOrders @CustomerId int AS SELECT Id, Total FROM dbo.Orders WHERE CustomerId = @CustomerId",
        "CREATE FUNCTION dbo.fn_Double (@x int) RETURNS int AS BEGIN RETURN @x * 2 END",
        "CREATE FUNCTION dbo.tvf_Orders (@CustomerId int) RETURNS TABLE AS RETURN (SELECT Id, Total FROM dbo.Orders WHERE CustomerId = @CustomerId)",
    ];

    private static Task? _setup;
    private static readonly object Gate = new();

    // Recreates the fixture database once per test run.
    public static Task EnsureCreatedAsync()
    {
        lock (Gate)
        {
            return _setup ??= CreateAsync(ServerConnection!);
        }
    }

    // Registers a user, creates a SQL connection to the fixture database, and returns the user's client and connection id.
    public static async Task<(HttpClient Client, Guid ConnectionId)> CreateConnectionAsync(ApiFactory factory, string userName, bool readOnly)
    {
        var client = await TestClients.AuthenticatedAsync(factory, userName);
        var parts = new SqlConnectionStringBuilder(ServerConnection!);
        var (host, port) = SplitDataSource(parts.DataSource);
        var response = await client.PostAsJsonAsync("/api/connections", new
        {
            name = "test-sql",
            host,
            port,
            database = DatabaseName,
            authMode = "SqlAuth",
            userName = parts.UserID,
            password = parts.Password,
            readOnly,
            trustServerCertificate = true,
            timeoutSec = 30,
        });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<NamedId>();
        return (client, created!.Id);
    }

    // Runs a T-SQL statement against the fixture database, for arranging and checking data in tests.
    public static async Task ExecuteAsync(string statement)
    {
        var db = new SqlConnectionStringBuilder(ServerConnection!) { InitialCatalog = DatabaseName }.ConnectionString;
        await using var connection = new SqlConnection(db);
        await connection.OpenAsync();
        await using var command = new SqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateAsync(string serverConnection)
    {
        var master = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = "master" }.ConnectionString;
        await ExecuteOn(master,
            "IF DB_ID(N'DbExplorerTests') IS NOT NULL BEGIN ALTER DATABASE DbExplorerTests SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE DbExplorerTests; END");
        await ExecuteOn(master, "CREATE DATABASE DbExplorerTests");

        var db = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = DatabaseName }.ConnectionString;
        foreach (var statement in Statements)
        {
            await ExecuteOn(db, statement);
        }
    }

    private static async Task ExecuteOn(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static (string Host, int Port) SplitDataSource(string dataSource)
    {
        var source = dataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
        var parts = source.Split(',');
        return (parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 1433);
    }

    private sealed record NamedId(Guid Id, string Name);
}
