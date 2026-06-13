using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for a student's exams (KONZEPT 3.4). Real exams count as attempts and a
/// failed one starts a repeat lock; preliminary exams are only noted. All
/// signed-in roles may use it; changes are audited.
/// </summary>
[ApiController]
[Route("api/students/{studentId:guid}/exams")]
public class ExamsController(IExamService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ExamListDto>> Get(Guid studentId, CancellationToken ct)
        => Ok(await service.GetForStudentAsync(studentId, ct));

    [HttpPost]
    public async Task<ActionResult<ExamListDto>> Create(Guid studentId, CreateExamRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(studentId, request, GetActor(), ct));

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
