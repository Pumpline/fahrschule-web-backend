using System.Net;
using System.Text.Json;
using Fahrschule.Application.Common;
using Fahrschule.Infrastructure.Identity;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;
using DomainPushSubscription = Fahrschule.Domain.Entities.PushSubscription;
using WebPushSubscription = WebPush.PushSubscription;

namespace Fahrschule.Application.Push;

public interface IPushService
{
    /// <summary>The public VAPID key the browser needs to subscribe (empty = off).</summary>
    string PublicKey { get; }

    /// <summary>Is push configured on the server (VAPID keys present)?</summary>
    bool IsConfigured { get; }

    Task SaveSubscriptionAsync(Guid userId, string endpoint, string p256dh, string auth, CancellationToken ct = default);
    Task RemoveSubscriptionAsync(Guid userId, string endpoint, CancellationToken ct = default);

    /// <summary>Sends a notification to every device of every Fahrlehrer
    /// (KONZEPT: only the instructor role gets push).</summary>
    Task SendToInstructorsAsync(string title, string body, string url, CancellationToken ct = default);
}

/// <summary>
/// Stores per-device push subscriptions and sends Web-Push notifications (signed
/// with the server's VAPID key). Dead subscriptions (404/410 from the push
/// service) are cleaned up automatically.
/// </summary>
public class PushService(
    FahrschuleDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<WebPushOptions> options,
    ILogger<PushService> logger) : IPushService
{
    private readonly WebPushOptions _options = options.Value;
    private readonly WebPushClient _client = new();

    public string PublicKey => _options.PublicKey;
    public bool IsConfigured => _options.IsConfigured;

    public async Task SaveSubscriptionAsync(
        Guid userId, string endpoint, string p256dh, string auth, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
        {
            throw new AppValidationException("Die Push-Anmeldung ist unvollständig. Bitte erneut versuchen.");
        }

        // One row per endpoint (per device). Re-subscribing just refreshes it.
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new DomainPushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveSubscriptionAsync(Guid userId, string endpoint, CancellationToken ct = default)
        => await db.PushSubscriptions
            .Where(s => s.Endpoint == endpoint && s.UserId == userId)
            .ExecuteDeleteAsync(ct);

    public async Task SendToInstructorsAsync(string title, string body, string url, CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        var instructorIds = (await userManager.GetUsersInRoleAsync(Roles.Fahrlehrer))
            .Select(u => u.Id).ToHashSet();
        if (instructorIds.Count == 0) return;

        var subs = await db.PushSubscriptions.Where(s => instructorIds.Contains(s.UserId)).ToListAsync(ct);
        if (subs.Count == 0) return;

        // Payload shape the Angular service worker understands (it calls
        // showNotification with this and opens the URL on click).
        var payload = JsonSerializer.Serialize(new
        {
            notification = new
            {
                title,
                body,
                icon = "/icons/icon-192.png",
                badge = "/icons/icon-192.png",
                data = new { onActionClick = new { @default = new { operation = "openWindow", url } } },
            },
        });
        var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);

        var dead = new List<DomainPushSubscription>();
        foreach (var s in subs)
        {
            try
            {
                await _client.SendNotificationAsync(
                    new WebPushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, vapid, ct);
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                dead.Add(s); // device unsubscribed / app removed → drop it
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Push an ein Gerät fehlgeschlagen.");
            }
        }

        if (dead.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(dead);
            await db.SaveChangesAsync(ct);
        }
    }
}
