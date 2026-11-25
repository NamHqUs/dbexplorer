using System.Net;
using System.Net.Http.Json;
using DbExplorer.Api.Contracts;

namespace DbExplorer.Api.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Passw0rd!Long";

    [Fact]
    public async Task Registration_is_open_only_for_the_first_account()
    {
        using var closedFactory = ApiFactory.WithRegistrationClosed();
        var client = closedFactory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("admin", Password));
        var second = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("intruder", Password));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_is_rotated_and_the_old_one_is_rejected()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("rotator", Password));
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("rotator", Password)))
            .Content.ReadFromJsonAsync<TokenResponse>();

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login!.RefreshToken));
        var reused = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Connections_require_authentication()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/connections");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Created_connection_never_returns_the_password_and_is_hidden_from_other_users()
    {
        var owner = await AuthenticatedClientAsync("owner-user");
        var other = await AuthenticatedClientAsync("other-user");

        var create = await owner.PostAsJsonAsync("/api/connections", SqlAuthRequest("dev-sql", "sa", "Secret!Pass1"));
        var body = await create.Content.ReadAsStringAsync();
        var id = (await create.Content.ReadFromJsonAsync<ConnectionIdOnly>())!.Id;

        var foreign = await other.GetAsync($"/api/connections/{id}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.DoesNotContain("Secret!Pass1", body);
        Assert.Contains("\"hasPassword\":true", body);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task SQL_auth_connection_without_password_is_rejected()
    {
        var client = await AuthenticatedClientAsync("validator");

        var response = await client.PostAsJsonAsync("/api/connections", SqlAuthRequest("no-pass", "sa", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Test_connection_reports_failure_for_unreachable_server_without_throwing()
    {
        var client = await AuthenticatedClientAsync("tester");
        var create = await client.PostAsJsonAsync("/api/connections", SqlAuthRequest("unreachable", "sa", "Secret!Pass1", port: 1, timeout: 2));
        var id = (await create.Content.ReadFromJsonAsync<ConnectionIdOnly>())!.Id;

        var response = await client.PostAsync($"/api/connections/{id}/test", null);
        var result = await response.Content.ReadFromJsonAsync<TestResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result!.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private Task<HttpClient> AuthenticatedClientAsync(string userName) => TestClients.AuthenticatedAsync(factory, userName);

    private static object SqlAuthRequest(string name, string user, string? password, int port = 1433, int timeout = 30) => new
    {
        name,
        host = "127.0.0.1",
        port,
        database = "master",
        authMode = "SqlAuth",
        userName = user,
        password,
        readOnly = false,
        trustServerCertificate = true,
        timeoutSec = timeout,
    };

    private sealed record ConnectionIdOnly(Guid Id);

    private sealed record TestResult(bool Success, string? Error);
}
