namespace DbExplorer.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "DbExplorer";
    public string Audience { get; set; } = "DbExplorer.Web";
    // Must be at least 32 characters. Required outside Development.
    public string? SigningKey { get; set; }
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
