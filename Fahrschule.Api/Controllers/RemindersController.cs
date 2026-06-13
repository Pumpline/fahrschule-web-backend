using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Reminders;
using Fahrschule.Contracts.Reminders;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// Follow-ups / reminders ("Wiedervorlagen", KONZEPT Stufe 2). Thin controller:
/// it only extracts the acting user and delegates to the service.
/// </summary>
[ApiController]
[Route("api/reminders")]
public class RemindersController(IReminderService service) : ControllerBase
{
    /// <summary>Follow-ups (open by default; optional include-done and per-student filter).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReminderDto>>> Get(
        [FromQuery] bool includeDone = false, [FromQuery] Guid? studentId = null, CancellationToken ct = default)
        => Ok(await service.GetListAsync(includeDone, studentId, ct));

    [HttpPost]
    public async Task<ActionResult<ReminderDto>> Create(SaveReminderRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, GetActor(), ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ReminderDto>> Update(Guid id, SaveReminderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, GetActor(), ct));

    /// <summary>Mark a follow-up done (or open again with ?done=false).</summary>
    [HttpPost("{id:guid}/erledigt")]
    public async Task<ActionResult<ReminderDto>> SetDone(Guid id, [FromQuery] bool done = true, CancellationToken ct = default)
        => Ok(await service.SetDoneAsync(id, done, GetActor(), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, GetActor(), ct);
        return NoContent();
    }

    private Actor GetActor()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var userId))
        {
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }
        return new Actor(userId, User.FindFirstValue(ClaimTypes.Name) ?? "Unbekannt");
    }
}
