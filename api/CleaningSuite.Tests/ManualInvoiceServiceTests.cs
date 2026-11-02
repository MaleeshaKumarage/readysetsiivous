using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using Moq;

namespace CleaningSuite.Tests.Invoicing;

public class ManualInvoiceServiceTests
{
    [Fact]
    public async Task Preview_rounds_each_net_then_vat_preserves_order_and_never_writes()
    {
        var f = new Fixture();
        var input = f.Input with { Lines = [new(" First ", 1.5m, .03m), new("Second", 2, 50), new("Included", 1, 0)] };
        var first = await f.Service.PreviewAsync(input, default);
        var second = await f.Service.PreviewAsync(input, default);
        Assert.Equal(first.PreviewToken, second.PreviewToken);
        Assert.Equal(new[] { "First", "Second", "Included" }, first.Invoice.Lines.Select(l => l.Description));
        Assert.Equal(.05m, first.Invoice.Lines[0].NetAmount); Assert.Equal(.01m, first.Invoice.Lines[0].VatAmount);
        Assert.Equal(100.05m, first.Invoice.Total.Net); Assert.Equal(25.51m, first.Invoice.Total.Vat); Assert.Equal(125.56m, first.Invoice.Total.Gross);
        Assert.Equal(Invoice.StatusDraft, first.Invoice.Status); Assert.Null(first.Invoice.PeriodFrom); Assert.Null(first.Invoice.PeriodTo);
        Assert.Equal(Guid.Empty, first.Invoice.BookingId); Assert.All(first.Invoice.Lines, l => Assert.Null(l.ShiftId));
        f.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Invoices.Verify(x => x.NextInvoiceNumberForYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Reservation.Verify(x => x.AcquireAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Issued")] [InlineData("Paid")] [InlineData("Voided")]
    public async Task Retry_returns_original_even_after_issuer_company_and_status_changes(string status)
    {
        var f = new Fixture(); var preview = await f.Service.PreviewAsync(f.Input, default);
        var input = f.Input with { PreviewToken = preview.PreviewToken };
        var first = await f.Service.IssueAsync(input, default); first.Status = status;
        f.Profile.CompanyName = "Changed"; f.Profile.CompanyAddress.Street = "Changed street";
        f.Company.Name = "Changed customer"; f.Companies.Reset();
        var retry = await f.Service.IssueAsync(input with { Lines = [new(" Service ", 2.00m, 50.00m)] }, default);
        Assert.Same(first, retry); Assert.Equal(status, retry.Status);
        Assert.Equal("Supplier", retry.Issuer.CompanyName); Assert.Equal("Supplier street", retry.Issuer.CompanyAddress.Street);
        Assert.Equal("Customer", retry.Customer.Name); Assert.Equal("2026-0007", first.InvoiceNumber);
        Assert.Equal(ShiftInvoiceService.PaymentReference("20260007"), first.PaymentReference);
        f.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Invoices.Verify(x => x.NextInvoiceNumberForYearAsync(2026, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reused_submission_with_changed_input_conflicts_but_new_id_is_independent()
    {
        var f = new Fixture(); var p = await f.Service.PreviewAsync(f.Input, default);
        await f.Service.IssueAsync(f.Input with { PreviewToken = p.PreviewToken }, default);
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Service.IssueAsync(f.Input with { Lines = [new("Different", 2, 50)], PreviewToken = p.PreviewToken }, default));
        var next = f.Input with { SubmissionId = Guid.NewGuid() }; var np = await f.Service.PreviewAsync(next, default);
        var invoice = await f.Service.IssueAsync(next with { PreviewToken = np.PreviewToken }, default);
        Assert.Equal(2, f.Saved.Count); Assert.NotEqual(f.Saved[0].Id, invoice.Id);
    }

    [Theory]
    [InlineData("issuer")] [InlineData("company")] [InlineData("vat")] [InlineData("terms")] [InlineData("rows")] [InlineData("address")] [InlineData("day")]
    public async Task Changes_require_a_new_preview(string change)
    {
        var f = new Fixture(); var preview = await f.Service.PreviewAsync(f.Input, default); var input = f.Input;
        switch (change)
        {
            case "issuer": f.Profile.CompanyName = "Other"; break;
            case "company": f.Company.BusinessId = "Other"; break;
            case "vat": f.Profile.DefaultVatRatePercent = 10; break;
            case "terms": f.Profile.PaymentTermsDays = 30; break;
            case "rows": input = input with { Lines = [new("Other", 2, 50)] }; break;
            case "address": input = input with { BillingAddress = new() { Street = "Other", City = "Helsinki", PostalCode = "00100", Country = "FI" } }; break;
            case "day": f.Now = f.Now.AddDays(1); break;
        }
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Service.IssueAsync(input with { PreviewToken = preview.PreviewToken }, default));
        Assert.Empty(f.Saved);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return ["empty rows"]; yield return ["too many rows"]; yield return ["null rows"]; yield return ["null row"];
        yield return ["description"]; yield return ["long description"]; yield return ["zero quantity"]; yield return ["negative quantity"];
        yield return ["large quantity"]; yield return ["quantity precision"]; yield return ["negative price"]; yield return ["large price"];
        yield return ["price precision"]; yield return ["free total"]; yield return ["null address"]; yield return ["incomplete address"];
        yield return ["long address"]; yield return ["language"]; yield return ["empty company"]; yield return ["empty submission"];
    }
    [Theory] [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_input_is_rejected_without_writes(string kind)
    {
        var f = new Fixture(); var r = f.Input;
        r = kind switch {
            "empty rows" => r with { Lines = [] }, "too many rows" => r with { Lines = Enumerable.Repeat(new ManualInvoiceRow("x", 1, 1), 101).ToArray() },
            "null rows" => r with { Lines = null }, "null row" => r with { Lines = [null!] },
            "description" => r with { Lines = [new(" ", 1, 1)] }, "long description" => r with { Lines = [new(new string('x', 1001), 1, 1)] },
            "zero quantity" => r with { Lines = [new("x", 0, 1)] }, "negative quantity" => r with { Lines = [new("x", -1, 1)] },
            "large quantity" => r with { Lines = [new("x", 10001, 1)] }, "quantity precision" => r with { Lines = [new("x", 1.001m, 1)] },
            "negative price" => r with { Lines = [new("x", 1, -1)] }, "large price" => r with { Lines = [new("x", 1, 1000001)] },
            "price precision" => r with { Lines = [new("x", 1, .001m)] }, "free total" => r with { Lines = [new("x", 1, 0)] },
            "null address" => r with { BillingAddress = null }, "incomplete address" => r with { BillingAddress = new() },
            "long address" => r with { BillingAddress = new() { Street = new string('x', 251), City = "x", PostalCode = "x", Country = "FI" } },
            "language" => r with { Language = "de" }, "empty company" => r with { CompanyId = Guid.Empty },
            "empty submission" => r with { SubmissionId = Guid.Empty }, _ => throw new InvalidOperationException()
        };
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(r, default)); Assert.Empty(f.Saved);
    }

    [Fact]
    public async Task Issue_requires_preview_and_valid_issuer_and_tenant_company()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.IssueAsync(f.Input, default));
        f.Reservation.Verify(x => x.AcquireAsync(It.IsAny<CancellationToken>()), Times.Never);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.PreviewAsync(f.Input with { CompanyId = Guid.NewGuid() }, default));
        f.Profile.BankAccountIBAN = "INVALID"; var p = await f.Service.PreviewAsync(f.Input, default);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.IssueAsync(f.Input with { PreviewToken = p.PreviewToken }, default));
        Assert.Empty(f.Saved);
    }

    [Fact]
    public async Task Tenant_local_new_year_uses_shared_yearly_counter_and_terms()
    {
        var f = new Fixture { Now = new(2026, 12, 31, 22, 30, 0, TimeSpan.Zero) };
        var p = await f.Service.PreviewAsync(f.Input, default); var invoice = await f.Service.IssueAsync(f.Input with { PreviewToken = p.PreviewToken }, default);
        Assert.Equal(new DateTime(2027, 1, 1), invoice.IssueDate); Assert.Equal(new DateTime(2027, 1, 15), invoice.DueDate);
        Assert.Equal("2027-0007", invoice.InvoiceNumber);
        f.Invoices.Verify(x => x.NextInvoiceNumberForYearAsync(2027, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1, 14)] [InlineData(50.01, 14)] [InlineData(25.555, 14)] [InlineData(25.5, -1)] [InlineData(25.5, 121)]
    public async Task Invalid_saved_vat_or_terms_cannot_generate_an_invoice(decimal vat, int terms)
    {
        var f = new Fixture(); f.Profile.DefaultVatRatePercent = vat; f.Profile.PaymentTermsDays = terms;
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(f.Input, default)); Assert.Empty(f.Saved);
    }

    [Fact]
    public async Task Maximum_rows_and_rates_are_bounded_without_overflow_and_zero_vat_is_supported()
    {
        var f = new Fixture(); f.Profile.DefaultVatRatePercent = 0;
        var p = await f.Service.PreviewAsync(f.Input with { Lines = Enumerable.Repeat(new ManualInvoiceRow("Agreed goods", 10000, 1000000), 100).ToArray() }, default);
        Assert.Equal(1000000000000m, p.Invoice.Total.Net); Assert.Equal(0, p.Invoice.Total.Vat);
        var canonical = await f.Service.PreviewAsync(f.Input with { Lines = [new("Service", 2.00m, 50.00m)] }, default);
        var original = await f.Service.PreviewAsync(f.Input, default); Assert.Equal(original.PreviewToken, canonical.PreviewToken);
    }

    internal sealed class Fixture
    {
        public Mock<IInvoiceRepository> Invoices = new(); public Mock<ICompanyRepository> Companies = new(); public Mock<IShiftWorkReservation> Reservation = new();
        public Company Company = new() { Id = Guid.NewGuid(), Name = "Customer", BusinessId = "1234567-8" };
        public TenantProfile Profile = new() { CompanyName = "Supplier", BusinessId = "1234567-8", TimeZoneId = "Europe/Helsinki", DefaultVatRatePercent = 25.5m,
            CompanyAddress = new() { Street = "Supplier street", PostalCode = "00100", City = "Helsinki", Country = "FI" }, BankAccountIBAN = "FI2112345600000785", BankBic = "NDEAFIHH", PaymentTermsDays = 14 };
        public List<Invoice> Saved = []; public DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        public ManualInvoiceRequest Input; public ManualInvoiceService Service;
        public Fixture()
        {
            Companies.Setup(x => x.GetAsync(Company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => Company);
            Invoices.Setup(x => x.GetByManualSubmissionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) => Saved.SingleOrDefault(i => i.ManualSubmissionId == id));
            Invoices.Setup(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>())).Callback<Invoice, CancellationToken>((i, _) => Saved.Add(i)).Returns(Task.CompletedTask);
            Invoices.Setup(x => x.NextInvoiceNumberForYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(7L);
            var profiles = new Mock<ITenantProfileRepository>(); profiles.Setup(x => x.GetAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(() => Profile);
            var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("test");
            var clock = new Mock<TimeProvider>(); clock.Setup(x => x.GetUtcNow()).Returns(() => Now);
            Reservation.Setup(x => x.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Lease());
            Input = new(Company.Id, Guid.NewGuid(), [new("Service", 2, 50)], new() { Street = "Customer street", PostalCode = "00100", City = "Helsinki", Country = "FI" });
            Service = new(Invoices.Object, Companies.Object, profiles.Object, tenant.Object, Reservation.Object, clock.Object);
        }
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
