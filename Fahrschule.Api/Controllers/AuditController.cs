using System.Security.Claims;
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
    /// <summary>Audit log, filterable + paginated (newest first). Each role only
    /// sees the categories configured for it (Admin sees all).</summary>
    [HttpGet]
    public async Task<ActionResult<AuditListResultDto>> Get(
        [FromQuery] string? search, [FromQuery] string? category,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        return Ok(await auditQuery.GetListAsync(roles, search, category, page, pageSize, ct));
    }
}
