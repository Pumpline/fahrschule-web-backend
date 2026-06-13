using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for a student's training progress (KONZEPT 3.3). The personal checklist
/// is a snapshot of the curriculum; here the Fahrlehrer/office tick off points
/// and count special drives. All signed-in roles may use it; changes are audited.
/// </summary>
[ApiController]
[Route("api/students/{studentId:guid}/progress")]
public class StudentProgressController(IStudentProgressService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StudentProgressDto>> Get(Guid studentId, CancellationToken ct)
        => Ok(await service.GetForStudentAsync(studentId, ct));

    /// <summary>Preview what adding a class would credit/require (KONZEPT 3.3a).</summary>
    [HttpGet("credit-preview/{licenseClassId:guid}")]
    public async Task<ActionResult<CreditPreviewDto>> CreditPreview(Guid studentId, Guid licenseClassId, CancellationToken ct)
        => Ok(await service.GetCreditPreviewAsync(studentId, licenseClassId, ct));

    /// <summary>Tick / untick a simple point (KONZEPT 3.3).</summary>
    [HttpPut("{itemId:guid}")]
    public async Task<ActionResult<StudentProgressDto>> SetItem(
        Guid studentId, Guid itemId, SetProgressItemRequest request, CancellationToken ct)
        => Ok(await service.SetItemAsync(studentId, itemId, request, GetActor(), ct));

    /// <summary>Add one counted session to a countable point (the "+" button).</summary>
    [HttpPost("{itemId:guid}/entries")]
    public async Task<ActionResult<StudentProgressDto>> AddEntry(
        Guid studentId, Guid itemId, AddProgressEntryRequest request, CancellationToken ct)
        => Ok(await service.AddEntryAsync(studentId, itemId, request, GetActor(), ct));

    /// <summary>Remove one counted session (the "−" button).</summary>
    [HttpDelete("{itemId:guid}/entries/{entryId:guid}")]
    public async Task<ActionResult<StudentProgressDto>> RemoveEntry(
        Guid studentId, Guid itemId, Guid entryId, CancellationToken ct)
        => Ok(await service.RemoveEntryAsync(studentId, itemId, entryId, GetActor(), ct));

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
