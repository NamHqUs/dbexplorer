using System.Net.Http.Headers;
using System.Net.Http.Json;
using DbExplorer.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace DbExplorer.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dbexplorer-tests-{Guid.NewGuid():N}.db");
    private readonly bool _allowRegistration;

    public ApiFactory() : this(allowRegistration: true) { }

    private ApiFactory(bool allowRegistration) => _allowRegistration = allowRegistration;

    public static ApiFactory WithRegistrationClosed() => new(allowRegistration: false);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:AppDb", $"Data Source={_dbPath}");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-at-least-32-bytes-long");
        builder.UseSetting("Auth:AllowRegistration", _allowRegistration.ToString());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}

public static class TestClients
{
    private const string Password = "Passw0rd!Long";

    // Registers and logs in a user, returning a client with a bearer token.
    public static async Task<HttpClient> AuthenticatedAsync(ApiFactory factory, string userName)
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(userName, Password));
        var tokens = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, Password)))
            .Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }
}
