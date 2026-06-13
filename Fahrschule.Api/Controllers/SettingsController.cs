using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Settings;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the operational settings (admin panel). All signed-in roles may
/// read (the values drive reminders/locks everywhere); only the admin writes.
/// </summary>
[ApiController]
[Route("api/settings")]
public class SettingsController(ISettingsService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AppSettingsDto>> Get(CancellationToken ct)
        => Ok(await service.GetAsync(ct));

    [HttpPut]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<AppSettingsDto>> Update(AppSettingsDto request, CancellationToken ct)
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var userId))
        {
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }
        var actor = new Actor(userId, User.FindFirstValue(ClaimTypes.Name) ?? "Unbekannt");
        return Ok(await service.UpdateAsync(request, actor, ct));
    }
}
