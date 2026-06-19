namespace Fahrschule.Domain.Entities;

/// <summary>
/// One Web-Push subscription of ONE device of a user (KONZEPT 3.5 push).
///
/// When the user enables appointment reminders on a device, the browser hands
/// out a unique push "address" at its vendor's push service (Google/Apple/
/// Mozilla). We store that here so a background job can later send a reminder to
/// exactly that device - even when the app is closed. One row per device; a
/// user can have several (phone, tablet). Data minimisation: only the technical
/// keys, no device fingerprint.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; }

    /// <summary>The user (Identity) this device belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>The push service endpoint URL the message is sent to (unique per
    /// device/subscription).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Public key of the subscription (browser-provided, "p256dh").</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Auth secret of the subscription (browser-provided, "auth").</summary>
    public string Auth { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
