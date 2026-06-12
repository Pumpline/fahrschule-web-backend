using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.Curriculum;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Curriculum;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API für die Ausbildungsplan-Punkte (Theorie-Themen, später Grundfahraufgaben
/// und Sonderfahrten). Lesen dürfen alle Angemeldeten (Fahrlehrer braucht den
/// Plan beim Abhaken), schreiben nur der Admin.
/// </summary>
[ApiController]
[Route("api/curriculum-items")]
public class CurriculumItemsController(ICurriculumItemService service) : ControllerBase
{
    /// <summary>Aktuell gültige Punkte, optional nach Abschnitt gefiltert
    /// (z. B. ?section=Theorie-Grundstoff).</summary>
    [HttpGet]
    public async Task<ActionResult<List<CurriculumItemDto>>> GetCurrent([FromQuery] string? section, CancellationToken ct)
        => Ok(await service.GetCurrentAsync(section, ct));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<CurriculumItemDto>> Create(CreateCurriculumItemRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, GetActor(), ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<CurriculumItemDto>> Update(Guid id, UpdateCurriculumItemRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, GetActor(), ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, GetActor(), ct);
        return NoContent();
    }

    private Actor GetActor()
    {
        var idWert = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idWert, out var userId))
        {
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }
        return new Actor(userId, User.FindFirstValue(ClaimTypes.Name) ?? "Unbekannt");
    }
}
