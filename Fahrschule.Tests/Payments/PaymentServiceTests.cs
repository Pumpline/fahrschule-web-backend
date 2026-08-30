using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Payments;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Payments;

/// <summary>
/// Money of a student (KONZEPT 3.6). The point of these tests is the border
/// between the two layers: open items may be corrected, an ISSUED receipt may
/// not - it can only be cancelled, which frees the items again.
/// </summary>
public class PaymentServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);

    private PaymentService NewService(FahrschuleDbContext db)
        => new(db, new SettingsService(db, new NullAuditWriter()), new NullAuditWriter());

    private readonly Guid _student = Guid.NewGuid();

    private async Task SeedAsync()
    {
        await using var db = NewDb();
        db.Students.Add(new Student { Id = _student, FirstName = "Max", LastName = "Muster" });
        await db.SaveChangesAsync();
    }

    private static SavePaymentItemRequest Item(string description, decimal gross, int rate = 19, int day = 1) => new()
    {
        DateOn = new DateOnly(2026, 3, day),
        Description = description,
        GrossAmount = gross,
        VatRatePercent = rate,
    };

    private async Task AddAsync(SavePaymentItemRequest request)
    {
        await using var db = NewDb();
        await NewService(db).AddItemAsync(_student, request, TestActor);
    }

    private async Task<PaymentOverviewDto> LoadAsync()
    {
        await using var db = NewDb();
        return await NewService(db).GetForStudentAsync(_student);
    }

    [Fact]
    public async Task Free_items_are_listed_as_open_with_their_vat_split()
    {
        await SeedAsync();
        await AddAsync(Item("Grundbetrag", 250m));
        await AddAsync(Item("Prüfungsgebühr", 119m));

        var view = await LoadAsync();

        Assert.Equal(2, view.OpenItems.Count);
        Assert.Equal(369m, view.OpenTotalGross);
        var fee = view.OpenItems.Single(i => i.Description == "Prüfungsgebühr");
        Assert.Equal(100m, fee.Net);
        Assert.Equal(19m, fee.VatAmount);
        Assert.Empty(view.Receipts);
    }

    [Fact]
    public async Task Issuing_a_receipt_numbers_it_and_empties_the_open_list()
    {
        await SeedAsync();
        await AddAsync(Item("Grundbetrag", 250m));
        await AddAsync(Item("Fahrstunde", 60m));

        await using (var db = NewDb())
        {
            await NewService(db).IssueReceiptAsync(_student, TestActor);
        }

        var view = await LoadAsync();
        var receipt = Assert.Single(view.Receipts);
        Assert.Equal(DateTime.UtcNow.Year + "-0001", receipt.Number);
        Assert.Equal(310m, receipt.TotalGross);
        Assert.Equal(2, receipt.Items.Count);
        Assert.Empty(view.OpenItems);           // everything is on the receipt now
        Assert.Equal(310m, view.ReceiptedTotalGross);
    }

    [Fact]
    public async Task Receipt_numbers_run_without_gaps()
    {
        await SeedAsync();
        for (var i = 1; i <= 3; i++)
        {
            await AddAsync(Item("Rate " + i, 100m));
            await using var db = NewDb();
            await NewService(db).IssueReceiptAsync(_student, TestActor);
        }

        var view = await LoadAsync();
        var numbers = view.Receipts.Select(r => r.Number).OrderBy(n => n).ToList();
        var year = DateTime.UtcNow.Year;
        Assert.Equal([$"{year}-0001", $"{year}-0002", $"{year}-0003"], numbers);
    }

    [Fact]
    public async Task An_item_on_a_receipt_can_no_longer_be_changed_or_deleted()
    {
        await SeedAsync();
        await AddAsync(Item("Grundbetrag", 250m));
        var itemId = (await LoadAsync()).OpenItems.Single().Id;

        await using (var db = NewDb())
        {
            await NewService(db).IssueReceiptAsync(_student, TestActor);
        }

        await using (var db = NewDb())
        {
            await Assert.ThrowsAsync<AppValidationException>(() =>
                NewService(db).UpdateItemAsync(_student, itemId, Item("Anders", 300m), TestActor));
        }
        await using (var db = NewDb())
        {
            await Assert.ThrowsAsync<AppValidationException>(() =>
                NewService(db).DeleteItemAsync(_student, itemId, TestActor));
        }
    }

    [Fact]
    public async Task Cancelling_writes_a_reversing_receipt_and_frees_the_items()
    {
        await SeedAsync();
        await AddAsync(Item("Grundbetrag", 250m));

        Guid receiptId;
        await using (var db = NewDb())
        {
            var view = await NewService(db).IssueReceiptAsync(_student, TestActor);
            receiptId = view.Receipts.Single().Id;
        }

        await using (var db = NewDb())
        {
            await NewService(db).CancelReceiptAsync(
                _student, receiptId, new CancelReceiptRequest { Reason = "Falscher Betrag" }, TestActor);
        }

        var after = await LoadAsync();

        // The original stays - it is never deleted (§ 147 AO).
        var original = after.Receipts.Single(r => !r.IsCancellation);
        var cancellation = after.Receipts.Single(r => r.IsCancellation);
        Assert.Equal(cancellation.Number, original.CancelledByNumber);
        Assert.Equal(original.Number, cancellation.CancelsNumber);
        Assert.Equal(-250m, cancellation.TotalGross);      // reverses the original
        Assert.Equal(0m, after.ReceiptedTotalGross);       // the two cancel out
        Assert.Single(after.OpenItems);                    // item is correctable again
    }

    [Fact]
    public async Task A_cancelled_receipt_cannot_be_cancelled_twice()
    {
        await SeedAsync();
        await AddAsync(Item("Grundbetrag", 250m));

        Guid receiptId;
        await using (var db = NewDb())
        {
            receiptId = (await NewService(db).IssueReceiptAsync(_student, TestActor)).Receipts.Single().Id;
        }
        await using (var db = NewDb())
        {
            await NewService(db).CancelReceiptAsync(
                _student, receiptId, new CancelReceiptRequest { Reason = "Tippfehler" }, TestActor);
        }
        await using (var db = NewDb())
        {
            await Assert.ThrowsAsync<AppValidationException>(() =>
                NewService(db).CancelReceiptAsync(
                    _student, receiptId, new CancelReceiptRequest { Reason = "Noch mal" }, TestActor));
        }
    }

    [Fact]
    public async Task A_receipt_needs_at_least_one_open_amount()
    {
        await SeedAsync();
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db).IssueReceiptAsync(_student, TestActor));
    }

    [Fact]
    public async Task An_empty_description_or_a_zero_amount_is_refused()
    {
        await SeedAsync();
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            service.AddItemAsync(_student, Item("   ", 50m), TestActor));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            service.AddItemAsync(_student, Item("Grundbetrag", 0m), TestActor));
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
