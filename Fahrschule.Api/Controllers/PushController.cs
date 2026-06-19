using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.Push;
using Fahrschule.Contracts.Push;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// Web-Push endpoints (KONZEPT 3.5): hand out the public VAPID key and let a
/// device register/unregister for appointment reminders. Reminders are opt-in
/// per device, so ANY signed-in user may enable them (owner decision 20.06.2026
/// - originally Fahrlehrer-only). Thin controller.
/// </summary>
[ApiController]
[Route("api/push")]
public class PushController(IPushService push) : ControllerBase
{
    /// <summary>The public VAPID key the browser needs to subscribe (empty = push off).</summary>
    [HttpGet("config")]
    public ActionResult<PushConfigDto> Config()
        => Ok(new PushConfigDto { PublicKey = push.PublicKey });

    /// <summary>Register THIS device for appointment reminders (any signed-in user).</summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(SavePushSubscriptionRequest request, CancellationToken ct)
    {
        await push.SaveSubscriptionAsync(GetUserId(), request.Endpoint, request.P256dh, request.Auth, ct);
        return NoContent();
    }

    /// <summary>Unregister THIS device again (any signed-in user, e.g. when turning it off).</summary>
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe(RemovePushSubscriptionRequest request, CancellationToken ct)
    {
        await push.RemoveSubscriptionAsync(GetUserId(), request.Endpoint, ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
        {
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }
        return userId;
    }
}
