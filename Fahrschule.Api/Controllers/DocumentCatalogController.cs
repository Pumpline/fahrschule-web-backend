using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.Documents;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Documents;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the document catalogue (admin panel). All signed-in roles may read
/// (the student file needs it later); only the admin may write.
/// </summary>
[ApiController]
[Route("api/document-catalog")]
public class DocumentCatalogController(IDocumentCatalogService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DocumentCatalogItemDto>>> GetAll(CancellationToken ct)
        => Ok(await service.GetAllAsync(ct));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<DocumentCatalogItemDto>> Create(CreateDocumentCatalogItemRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, GetActor(), ct));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<DocumentCatalogItemDto>> Update(Guid id, UpdateDocumentCatalogItemRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, GetActor(), ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
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
