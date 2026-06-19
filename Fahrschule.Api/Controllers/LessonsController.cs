using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for a student's recorded lessons (KONZEPT 3.3). Entering a lesson is the
/// single place where training is recorded; saving it ticks/counts the covered
/// points. All signed-in roles may use it; changes are audited.
/// </summary>
[ApiController]
[Route("api/students/{studentId:guid}/lessons")]
public class LessonsController(ILessonService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LessonDto>>> Get(Guid studentId, CancellationToken ct)
        => Ok(await service.GetForStudentAsync(studentId, ct));

    [HttpPost]
    public async Task<ActionResult<LessonDto>> Create(Guid studentId, CreateLessonRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(studentId, request, GetActor(), ct));

    [HttpPut("{lessonId:guid}")]
    public async Task<ActionResult<LessonDto>> Update(Guid studentId, Guid lessonId, UpdateLessonRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(studentId, lessonId, request, GetActor(), ct));

    [HttpDelete("{lessonId:guid}")]
    public async Task<IActionResult> Delete(Guid studentId, Guid lessonId, CancellationToken ct)
    {
        await service.DeleteAsync(studentId, lessonId, GetActor(), ct);
        return NoContent();
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
