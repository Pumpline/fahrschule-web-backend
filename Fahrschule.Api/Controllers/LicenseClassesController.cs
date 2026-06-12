using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.LicenseClasses;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the licence classes (admin panel).
///
/// All signed-in roles may read (instructors/office staff need the classes
/// everywhere later); only the admin may write - role-based access following
/// the least-privilege principle (project rule 1).
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

    /// <summary>Who is calling? (for the audit log - from the token claims)</summary>
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
