namespace Fahrschule.Infrastructure.Identity;

/// <summary>
/// Ein Refresh-Token: der "Schlüssel zum Nachschlüssel".
///
/// Hintergrund (web-typisches Konzept): Das eigentliche Zugriffstoken (JWT)
/// ist nur kurz gültig (z. B. 15 Minuten) – wird es gestohlen, ist der Schaden
/// begrenzt. Damit sich niemand alle 15 Minuten neu anmelden muss, gibt es
/// dieses langlebige Refresh-Token. Der Browser schickt es (als httpOnly-Cookie)
/// an /api/auth/refresh und bekommt ein frisches Zugriffstoken.
///
/// Sicherheit:
/// - Wir speichern nur den SHA-256-HASH des Tokens, nie den Klartext
///   (gleiches Prinzip wie bei Passwörtern: ein Datenbank-Leck verrät nichts).
/// - Bei jeder Verwendung wird das Token "rotiert": das alte wird ungültig,
///   ein neues wird ausgestellt. Ein gestohlenes, bereits benutztes Token
///   ist damit wertlos.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>Zu welchem Benutzer gehört das Token?</summary>
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>SHA-256-Hash des Token-Werts (hex). Der Klartext existiert nur im Cookie des Benutzers.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Gesetzt, sobald das Token zurückgezogen wurde (Abmelden, Rotation, Passwortwechsel).</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Bei Rotation: Hash des Nachfolger-Tokens (hilft, Diebstahl zu erkennen).</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Ist das Token zum Zeitpunkt <paramref name="nowUtc"/> noch verwendbar?</summary>
    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;
}
