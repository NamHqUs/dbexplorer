using DbExplorer.Application.Abstractions;
using DbExplorer.Application.Contracts;
using DbExplorer.Application.Domain;
using Microsoft.Data.SqlClient;

namespace DbExplorer.Infrastructure.SqlServer;

public static class SqlServerConnectionStringFactory
{
    public static SqlConnectionStringBuilder Build(DbConnection connection, string? password)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = connection.Port is { } port ? $"{connection.Host},{port}" : connection.Host,
            ConnectTimeout = connection.TimeoutSec,
            TrustServerCertificate = connection.TrustServerCertificate,
            ApplicationName = "DbExplorer",
        };

        if (!string.IsNullOrWhiteSpace(connection.Database))
        {
            builder.InitialCatalog = connection.Database;
        }

        if (connection.AuthMode == DbAuthMode.WindowsAuth)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = connection.UserName ?? string.Empty;
            builder.Password = password ?? string.Empty;
        }

        return builder;
    }
}

public sealed class SqlServerConnectionTester : ISqlServerConnectionTester
{
    public async Task<TestConnectionResult> TestAsync(DbConnection connection, string? password, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var builder = SqlServerConnectionStringFactory.Build(connection, password);
            await using var sql = new SqlConnection(builder.ConnectionString);
            await sql.OpenAsync(ct);

            await using var cmd = new SqlCommand("SELECT @@VERSION", sql) { CommandTimeout = connection.TimeoutSec };
            var version = (string?)await cmd.ExecuteScalarAsync(ct);

            return new TestConnectionResult(true, version, null, Elapsed(started));
        }
        catch (SqlException ex)
        {
            return new TestConnectionResult(false, null, ex.Message, Elapsed(started));
        }
        catch (InvalidOperationException ex)
        {
            return new TestConnectionResult(false, null, ex.Message, Elapsed(started));
        }
    }

    private static long Elapsed(long started) =>
        (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
