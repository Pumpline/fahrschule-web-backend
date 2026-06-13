using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Theory;
using Fahrschule.Contracts.Theory;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// Theory attendance shortcut ("Theorie-Anwesenheit", KONZEPT Stufe 2): tick one
/// theory topic for several students at once. Thin controller.
/// </summary>
[ApiController]
[Route("api/theory-attendance")]
public class TheoryAttendanceController(ITheoryAttendanceService service) : ControllerBase
{
    /// <summary>Theory topics to choose for ticking.</summary>
    [HttpGet("topics")]
    public async Task<ActionResult<List<TheoryTopicDto>>> Topics(CancellationToken ct)
        => Ok(await service.GetTopicsAsync(ct));

    /// <summary>Tick the chosen topic for the present students.</summary>
    [HttpPost("tick")]
    public async Task<ActionResult<TheoryTickResultDto>> Tick(TickTheoryRequest request, CancellationToken ct)
        => Ok(await service.TickAsync(request, GetActor(), ct));

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
