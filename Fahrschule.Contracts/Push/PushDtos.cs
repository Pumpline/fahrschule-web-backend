namespace Fahrschule.Contracts.Push;

/// <summary>The public VAPID key the browser needs to subscribe (not secret).</summary>
public class PushConfigDto
{
    /// <summary>The application server's public VAPID key (base64url), or empty
    /// when push is not configured on the server.</summary>
    public string PublicKey { get; set; } = string.Empty;
}

/// <summary>"Register this device for push" - the browser subscription, flattened
/// (the browser gives endpoint + keys.p256dh + keys.auth).</summary>
public class SavePushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

/// <summary>"Unregister this device" - identified by its endpoint.</summary>
public class RemovePushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
}
