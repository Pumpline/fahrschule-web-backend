using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for a student's document checklist (KONZEPT 3.1). The required
/// documents are derived from the catalogue and the student's classes; only
/// the status is stored. All signed-in roles may use it; changes are audited.
/// </summary>
[ApiController]
[Route("api/students/{studentId:guid}/documents")]
public class StudentDocumentsController(IStudentDocumentService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<StudentDocumentDto>>> Get(Guid studentId, CancellationToken ct)
        => Ok(await service.GetForStudentAsync(studentId, ct));

    [HttpPut("{catalogItemId:guid}")]
    public async Task<ActionResult<List<StudentDocumentDto>>> SetStatus(
        Guid studentId, Guid catalogItemId, SetStudentDocumentRequest request, CancellationToken ct)
        => Ok(await service.SetStatusAsync(studentId, catalogItemId, request, GetActor(), ct));

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
