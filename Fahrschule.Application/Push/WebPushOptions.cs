namespace Fahrschule.Application.Push;

/// <summary>
/// VAPID configuration for Web Push (KONZEPT 3.5). The key pair is generated
/// ONCE and lives in configuration/environment, NEVER in the repository (the
/// private key signs outgoing pushes - it is a secret like the JWT key). When
/// the keys are missing, push is simply switched off.
/// </summary>
public class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>Public VAPID key (base64url) - handed to the browser to subscribe.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Private VAPID key (base64url) - SECRET, signs outgoing pushes.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Contact the push services can reach us at (mailto: or https URL).</summary>
    public string Subject { get; set; } = "mailto:admin@fahrschule.local";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
