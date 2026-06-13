using System.Security.Claims;
using Fahrschule.Application.Admin;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Admin;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// Admin-only GDPR functions (KONZEPT 3.7): audit-log view, the "marked for
/// deletion" list with restore, and the per-student data export. No separate
/// GDPR centre - everything lives in the admin panel.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin")]
public class AdminController(
    IAuditQueryService auditQuery,
    IStudentService students,
    IStudentExportService export) : ControllerBase
{
    /// <summary>Audit log, filterable + paginated (newest first).</summary>
    [HttpGet("audit")]
    public async Task<ActionResult<AuditListResultDto>> Audit(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await auditQuery.GetListAsync(search, page, pageSize, ct));

    /// <summary>Students marked for deletion ("Zur Löschung vorgemerkt").</summary>
    [HttpGet("students/deleted")]
    public async Task<ActionResult<List<DeletedStudentDto>>> DeletedStudents(CancellationToken ct)
        => Ok(await students.GetDeletedAsync(ct));

    /// <summary>Undo a soft delete.</summary>
    [HttpPost("students/{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await students.RestoreAsync(id, GetActor(), ct);
        return NoContent();
    }

    /// <summary>Export all data of a student (GDPR access/portability).</summary>
    [HttpGet("students/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var (content, fileName) = await export.ExportAsync(id, GetActor(), ct);
        return File(content, "application/json", fileName);
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
