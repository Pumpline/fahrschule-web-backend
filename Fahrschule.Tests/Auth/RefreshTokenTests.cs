using Fahrschule.Infrastructure.Identity;

namespace Fahrschule.Tests.Auth;

/// <summary>Tests for the validity rules of a refresh token.</summary>
public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshToken CreateToken() => new()
    {
        CreatedAtUtc = Now.AddDays(-1),
        ExpiresAtUtc = Now.AddDays(13),
    };

    [Fact]
    public void Fresh_token_is_active()
    {
        Assert.True(CreateToken().IsActive(Now));
    }

    [Fact]
    public void Expired_token_is_inactive()
    {
        var token = CreateToken();
        var afterExpiry = token.ExpiresAtUtc.AddSeconds(1);

        Assert.False(token.IsActive(afterExpiry));
    }

    [Fact]
    public void Revoked_token_is_inactive()
    {
        // Revoked = signed out, rotated, or password changed.
        var token = CreateToken();
        token.RevokedAtUtc = Now;

        Assert.False(token.IsActive(Now));
    }
}
