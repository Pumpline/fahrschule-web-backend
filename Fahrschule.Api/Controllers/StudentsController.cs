using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the student module (KONZEPT 3.1). All signed-in roles may use it
/// (instructors and office staff both work with students); there is no
/// admin-only restriction here, but every change is audited.
/// </summary>
[ApiController]
[Route("api/students")]
public class StudentsController(IStudentService service) : ControllerBase
{
    /// <summary>Paged, filterable student list. Phases are passed as repeated
    /// query values, e.g. ?phase=Theory&amp;phase=Practice.</summary>
    [HttpGet]
    public async Task<ActionResult<StudentListResultDto>> GetList(
        [FromQuery] string? search,
        [FromQuery] Guid? classId,
        [FromQuery] string[]? phase,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        var phases = ParsePhases(phase);
        var query = new StudentListQuery(search, classId, phases, page, pageSize);
        return Ok(await service.GetListAsync(query, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StudentAkteDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await service.GetAkteAsync(id, ct));

    /// <summary>Reveal one sensitive master-data field on demand. The access is
    /// recorded in the audit log (KONZEPT 3.1 / DSGVO Zugriffsprotokoll).</summary>
    [HttpGet("{id:guid}/stammdaten/{field}")]
    public async Task<ActionResult<StudentFieldValueDto>> GetField(Guid id, string field, CancellationToken ct)
        => Ok(await service.GetFieldAsync(id, field, GetActor(), ct));

    [HttpPost]
    public async Task<ActionResult<StudentAkteDto>> Create(CreateStudentRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, GetActor(), ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StudentAkteDto>> Update(Guid id, UpdateStudentRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, GetActor(), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, GetActor(), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/classes")]
    public async Task<ActionResult<StudentAkteDto>> AddClass(Guid id, AddStudentLicenseClassRequest request, CancellationToken ct)
        => Ok(await service.AddLicenseClassAsync(id, request.LicenseClassId, GetActor(), ct));

    [HttpDelete("{id:guid}/classes/{licenseClassId:guid}")]
    public async Task<ActionResult<StudentAkteDto>> RemoveClass(Guid id, Guid licenseClassId, CancellationToken ct)
        => Ok(await service.RemoveLicenseClassAsync(id, licenseClassId, GetActor(), ct));

    // Vorbesitz: edited in the progress tab next to the training classes, so it
    // has its own endpoint - one call sets the whole block.
    [HttpPut("{id:guid}/vorbesitz")]
    public async Task<ActionResult<StudentAkteDto>> SetPriorLicense(
        Guid id, SetStudentPriorLicenseRequest request, CancellationToken ct)
        => Ok(await service.SetPriorLicenseAsync(id, request, GetActor(), ct));

    [HttpPut("{id:guid}/classes/{licenseClassId:guid}/phase")]
    public async Task<ActionResult<StudentAkteDto>> SetPhase(Guid id, Guid licenseClassId, SetStudentPhaseRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<StudentPhase>(request.Phase, out var phase))
        {
            throw new AppValidationException("Unbekannte Phase. Bitte die Seite neu laden.");
        }
        return Ok(await service.SetPhaseAsync(id, licenseClassId, phase, GetActor(), ct));
    }

    private static IReadOnlyList<StudentPhase>? ParsePhases(string[]? phase)
    {
        if (phase is null || phase.Length == 0) return null;
        var result = new List<StudentPhase>();
        foreach (var value in phase)
        {
            if (Enum.TryParse<StudentPhase>(value, out var parsed))
            {
                result.Add(parsed);
            }
        }
        return result.Count > 0 ? result : null;
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
