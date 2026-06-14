using System.Security.Claims;
using Fahrschule.Application.Dashboard;
using Fahrschule.Contracts.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the start-page dashboard (KONZEPT 3.1/3a): "Bald fällig" and
/// "Letzte Änderungen". Available to all signed-in roles.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController(IDashboardService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct)
        => Ok(await service.GetAsync(User.FindAll(ClaimTypes.Role).Select(c => c.Value), ct));
}
