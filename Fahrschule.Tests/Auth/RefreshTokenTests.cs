using Fahrschule.Infrastructure.Identity;

namespace Fahrschule.Tests.Auth;

/// <summary>Tests für die Gültigkeitsregeln eines Refresh-Tokens.</summary>
public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshToken CreateToken() => new()
    {
        CreatedAtUtc = Now.AddDays(-1),
        ExpiresAtUtc = Now.AddDays(13),
    };

    [Fact]
    public void Frisches_Token_ist_aktiv()
    {
        Assert.True(CreateToken().IsActive(Now));
    }

    [Fact]
    public void Abgelaufenes_Token_ist_inaktiv()
    {
        var token = CreateToken();
        var afterExpiry = token.ExpiresAtUtc.AddSeconds(1);

        Assert.False(token.IsActive(afterExpiry));
    }

    [Fact]
    public void Zurueckgezogenes_Token_ist_inaktiv()
    {
        // Zurückgezogen = abgemeldet, rotiert oder Passwort geändert.
        var token = CreateToken();
        token.RevokedAtUtc = Now;

        Assert.False(token.IsActive(Now));
    }
}
