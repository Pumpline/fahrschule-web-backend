using System.Security.Claims;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Payments;
using Fahrschule.Application.Pdf;
using Fahrschule.Contracts.Students;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// API for a student's money (KONZEPT 3.6): the paid items that are not on a
/// receipt yet, and the issued receipts themselves.
///
/// Note the asymmetry - it mirrors the law: items can be changed and deleted,
/// a receipt cannot. There is deliberately NO PUT and NO DELETE for a receipt;
/// the only way back is "cancel", which writes a second, reversing receipt.
/// All signed-in roles may use it; everything is audited.
/// </summary>
[ApiController]
[Route("api/students/{studentId:guid}")]
public class PaymentsController(IPaymentService service, IReceiptPdfService pdf) : ControllerBase
{
    [HttpGet("payments")]
    public async Task<ActionResult<PaymentOverviewDto>> Get(Guid studentId, CancellationToken ct)
        => Ok(await service.GetForStudentAsync(studentId, ct));

    [HttpPost("payments")]
    public async Task<ActionResult<PaymentOverviewDto>> AddItem(
        Guid studentId, SavePaymentItemRequest request, CancellationToken ct)
        => Ok(await service.AddItemAsync(studentId, request, GetActor(), ct));

    [HttpPut("payments/{itemId:guid}")]
    public async Task<ActionResult<PaymentOverviewDto>> UpdateItem(
        Guid studentId, Guid itemId, SavePaymentItemRequest request, CancellationToken ct)
        => Ok(await service.UpdateItemAsync(studentId, itemId, request, GetActor(), ct));

    [HttpDelete("payments/{itemId:guid}")]
    public async Task<ActionResult<PaymentOverviewDto>> DeleteItem(
        Guid studentId, Guid itemId, CancellationToken ct)
        => Ok(await service.DeleteItemAsync(studentId, itemId, GetActor(), ct));

    [HttpPost("receipts")]
    public async Task<ActionResult<PaymentOverviewDto>> IssueReceipt(Guid studentId, CancellationToken ct)
        => Ok(await service.IssueReceiptAsync(studentId, GetActor(), ct));

    [HttpPost("receipts/{receiptId:guid}/cancel")]
    public async Task<ActionResult<PaymentOverviewDto>> CancelReceipt(
        Guid studentId, Guid receiptId, CancelReceiptRequest request, CancellationToken ct)
        => Ok(await service.CancelReceiptAsync(studentId, receiptId, request, GetActor(), ct));

    [HttpGet("receipts/{receiptId:guid}/pdf")]
    public async Task<IActionResult> ReceiptPdf(Guid studentId, Guid receiptId, CancellationToken ct)
    {
        var (content, fileName) = await pdf.GenerateAsync(studentId, receiptId, ct);
        return File(content, "application/pdf", fileName);
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
