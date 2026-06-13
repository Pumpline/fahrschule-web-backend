using System.Security.Claims;
using Fahrschule.Application.Calendar;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Calendar;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the calendar (KONZEPT 3.5). One Fahrlehrer for now → global calendar;
/// double bookings are rejected by the service. All signed-in roles may use it;
/// changes are audited.
/// </summary>
[ApiController]
[Route("api/calendar")]
public class CalendarController(ICalendarService service) : ControllerBase
{
    /// <summary>All appointments of one month (e.g. ?year=2026&month=6).</summary>
    [HttpGet]
    public async Task<ActionResult<List<CalendarEventDto>>> Get([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
        => Ok(await service.GetForMonthAsync(year, month, ct));

    [HttpPost]
    public async Task<ActionResult<CalendarEventDto>> Create(SaveCalendarEventRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, GetActor(), ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CalendarEventDto>> Update(Guid id, SaveCalendarEventRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, GetActor(), ct));

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
