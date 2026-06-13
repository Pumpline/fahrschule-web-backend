using Fahrschule.Application.Audit;
using Fahrschule.Contracts.Admin;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// The change log / audit trail ("Änderungsprotokoll", KONZEPT 3.7). Read-only
/// and available to all signed-in roles (Admin, Fahrlehrer, Verwaltung) - the
/// owner decided the office and instructors should be able to see who changed
/// what. There is no write endpoint: entries are only ever produced by the
/// system itself (IAuditWriter).
/// </summary>
[ApiController]
[Authorize(Roles = $"{Roles.Admin},{Roles.Fahrlehrer},{Roles.Verwaltung}")]
[Route("api/audit")]
public class AuditController(IAuditQueryService auditQuery) : ControllerBase
{
    /// <summary>Audit log, filterable + paginated (newest first).</summary>
    [HttpGet]
    public async Task<ActionResult<AuditListResultDto>> Get(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await auditQuery.GetListAsync(search, page, pageSize, ct));
}
