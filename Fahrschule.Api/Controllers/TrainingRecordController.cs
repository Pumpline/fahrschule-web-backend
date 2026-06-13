using Fahrschule.Application.Pdf;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for the printable Ausbildungsnachweis (training record, KONZEPT 3.3/7).
/// Returns a PDF of the student's progress per class plus the exams.
/// </summary>
[ApiController]
[Route("api/students/{studentId:guid}/ausbildungsnachweis")]
public class TrainingRecordController(ITrainingRecordPdfService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid studentId, CancellationToken ct)
    {
        var (content, fileName) = await service.GenerateAsync(studentId, ct);
        return File(content, "application/pdf", fileName);
    }
}
