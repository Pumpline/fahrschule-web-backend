using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.LicenseClasses;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API für die Führerscheinklassen (Adminpanel).
///
/// Lesen dürfen alle angemeldeten Rollen (Fahrlehrer/Verwaltung brauchen die
/// Klassen später überall), ändern darf nur der Admin – rollenbasierter
/// Zugriff nach dem Prinzip der geringsten Rechte (Projektregel 1).
/// </summary>
[ApiController]
[Route("api/license-classes")]
public class LicenseClassesController(ILicenseClassService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LicenseClassDto>>> GetAll(CancellationToken ct)
        => Ok(await service.GetAllAsync(ct));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<LicenseClassDto>> Create(CreateLicenseClassRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, GetActor(), ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<LicenseClassDto>> Update(Guid id, UpdateLicenseClassRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, GetActor(), ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, GetActor(), ct);
        return NoContent();
    }

    /// <summary>Wer ruft auf? (für das Audit-Log – aus den Token-Claims)</summary>
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
