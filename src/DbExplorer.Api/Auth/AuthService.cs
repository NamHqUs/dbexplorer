using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DbExplorer.Api.Contracts;
using DbExplorer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DbExplorer.Api.Auth;

// Refresh tokens live in AspNetUserTokens (provider "DbExplorer"), stored as "<sha256>|<expiresUnix>".
// Only the hash is persisted. Each refresh rotates the token.
public sealed class AuthService(
    UserManager<IdentityUser> users,
    AppDbContext db,
    JwtOptions options,
    TimeProvider clock)
{
    private const string TokenProvider = "DbExplorer";
    private const string RefreshTokenName = "RefreshToken";

    public async Task<bool> CanRegisterAsync(bool allowRegistration, CancellationToken ct)
    {
        if (allowRegistration) return true;
        // Bootstrap: the first account can be created without configuration.
        return !await users.Users.AnyAsync(ct);
    }

    public async Task<IdentityResult> RegisterAsync(RegisterRequest request)
    {
        var user = new IdentityUser { UserName = request.UserName.Trim() };
        return await users.CreateAsync(user, request.Password);
    }

    public async Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(request.UserName.Trim());
        if (user is null || !await users.CheckPasswordAsync(user, request.Password)) return null;
        return await IssueAsync(user, ct);
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = HashToken(refreshToken);
        var stored = await db.UserTokens
            .FirstOrDefaultAsync(t => t.LoginProvider == TokenProvider && t.Name == RefreshTokenName && t.Value!.StartsWith(hash + "|"), ct);
        if (stored is null) return null;

        var expiresUnix = long.Parse(stored.Value!.Split('|')[1]);
        if (clock.GetUtcNow().ToUnixTimeSeconds() >= expiresUnix) return null;

        var user = await users.FindByIdAsync(stored.UserId);
        if (user is null) return null;

        // Rotation: the old token is replaced by the one issued below.
        return await IssueAsync(user, ct);
    }

    private async Task<TokenResponse> IssueAsync(IdentityUser user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var accessExpires = now.AddMinutes(options.AccessTokenMinutes);
        var refreshExpires = now.AddDays(options.RefreshTokenDays);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var value = $"{HashToken(refreshToken)}|{refreshExpires.ToUnixTimeSeconds()}";
        await users.SetAuthenticationTokenAsync(user, TokenProvider, RefreshTokenName, value);

        return new TokenResponse(CreateAccessToken(user, accessExpires), accessExpires, refreshToken, refreshExpires);
    }

    private string CreateAccessToken(IdentityUser user, DateTimeOffset expires)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey!));
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: clock.GetUtcNow().UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
