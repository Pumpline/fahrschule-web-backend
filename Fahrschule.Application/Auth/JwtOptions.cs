namespace Fahrschule.Application.Auth;

/// <summary>
/// Settings for token generation - loaded from appsettings/environment
/// variables (section "Jwt"). The secret key material NEVER lives in code
/// or in the repository (project rule: no secrets in the repo).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Who issues the token? (our API)</summary>
    public string Issuer { get; set; } = "Fahrschule.Api";

    /// <summary>Who is it intended for? (our frontend)</summary>
    public string Audience { get; set; } = "Fahrschule.Frontend";

    /// <summary>Secret signing key (at least 32 characters).
    /// The signature guarantees nobody can forge tokens.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime - keep it short (limits damage if stolen).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token lifetime - how long you stay signed in without logging in again.</summary>
    public int RefreshTokenDays { get; set; } = 14;
}
