using DbExplorer.Api.Auth;
using DbExplorer.Api.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DbExplorer.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, IConfiguration configuration) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var allowRegistration = configuration.GetValue<bool>("Auth:AllowRegistration");
        if (!await auth.CanRegisterAsync(allowRegistration, ct))
            return Problem(title: "Registration is disabled.", statusCode: StatusCodes.Status403Forbidden);

        var result = await auth.RegisterAsync(request);
        if (!result.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(ToErrors(result)));

        return StatusCode(StatusCodes.Status201Created);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var tokens = await auth.LoginAsync(request, ct);
        return tokens is null
            ? Problem(title: "Invalid user name or password.", statusCode: StatusCodes.Status401Unauthorized)
            : Ok(tokens);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var tokens = await auth.RefreshAsync(request.RefreshToken, ct);
        return tokens is null
            ? Problem(title: "Refresh token is invalid or expired.", statusCode: StatusCodes.Status401Unauthorized)
            : Ok(tokens);
    }

    private static Dictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "Password" : "UserName")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
