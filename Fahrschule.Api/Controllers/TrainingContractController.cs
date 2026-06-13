using Fahrschule.Application.Pdf;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the printable Ausbildungsvertrag (training contract, KONZEPT 1a/3a).
/// Returns a PDF with the parties, requested classes, the editable contract terms
/// and signature lines.
/// </summary>
[ApiController]
[Route("api/students/{studentId:guid}/ausbildungsvertrag")]
public class TrainingContractController(ITrainingContractPdfService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid studentId, CancellationToken ct)
    {
        var (content, fileName) = await service.GenerateAsync(studentId, ct);
        return File(content, "application/pdf", fileName);
    }
}
