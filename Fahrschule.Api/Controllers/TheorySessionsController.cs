using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Theory;
using Fahrschule.Contracts.Theory;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// Theory attendance lists ("Theorie-Anwesenheitslisten", KONZEPT Stufe 2). Thin
/// controller: extract the acting user and delegate to the service.
/// </summary>
[ApiController]
[Route("api/theory-sessions")]
public class TheorySessionsController(ITheorySessionService service) : ControllerBase
{
    /// <summary>Theory topics to choose for a session.</summary>
    [HttpGet("topics")]
    public async Task<ActionResult<List<TheoryTopicDto>>> Topics(CancellationToken ct)
        => Ok(await service.GetTopicsAsync(ct));

    [HttpGet]
    public async Task<ActionResult<List<TheorySessionListItemDto>>> Get(CancellationToken ct)
        => Ok(await service.GetListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TheorySessionDetailDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<TheorySessionDetailDto>> Create(CreateTheorySessionRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, GetActor(), ct));

    [HttpPost("{id:guid}/attendees")]
    public async Task<ActionResult<TheorySessionDetailDto>> AddAttendees(Guid id, AddAttendeesRequest request, CancellationToken ct)
        => Ok(await service.AddAttendeesAsync(id, request, GetActor(), ct));

    [HttpDelete("{id:guid}/attendees/{studentId:guid}")]
    public async Task<ActionResult<TheorySessionDetailDto>> RemoveAttendee(Guid id, Guid studentId, CancellationToken ct)
        => Ok(await service.RemoveAttendeeAsync(id, studentId, GetActor(), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, GetActor(), ct);
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
