using System;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Invoicing.Commands;
using CleaningSuite.Domain.Invoicing;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Invoicing;

public class InvoiceStatusRulesTests
{
    private static (Mock<IInvoiceRepository> repo, Invoice invoice) Setup(string status)
    {
        var invoice = new Invoice { Id = Guid.NewGuid(), Status = status };
        var repo = new Mock<IInvoiceRepository>();
        repo.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return (repo, invoice);
    }

    [Fact]
    public async Task Voiding_a_paid_invoice_is_refused_and_keeps_it_paid()
    {
        var (repo, invoice) = Setup(Invoice.StatusPaid);
        var ex = await Assert.ThrowsAsync<InvoiceConflictException>(() =>
            new VoidInvoiceHandler(repo.Object).Handle(new VoidInvoiceCommand(invoice.Id), CancellationToken.None));
        Assert.Contains("already paid", ex.Message);
        Assert.Equal(Invoice.StatusPaid, invoice.Status);
        repo.Verify(r => r.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Voiding_an_issued_invoice_still_works()
    {
        var (repo, invoice) = Setup(Invoice.StatusIssued);
        await new VoidInvoiceHandler(repo.Object).Handle(new VoidInvoiceCommand(invoice.Id), CancellationToken.None);
        Assert.Equal(Invoice.StatusVoided, invoice.Status);
    }

    [Fact]
    public async Task Marking_paid_without_a_date_stores_now_not_the_default_value()
    {
        var (repo, invoice) = Setup(Invoice.StatusIssued);
        var before = DateTime.UtcNow;
        await new MarkInvoicePaidHandler(repo.Object).Handle(new MarkInvoicePaidCommand(invoice.Id, default), CancellationToken.None);
        Assert.Equal(Invoice.StatusPaid, invoice.Status);
        Assert.NotNull(invoice.PaidAtUtc);
        Assert.True(invoice.PaidAtUtc >= before && invoice.PaidAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Marking_paid_keeps_the_given_date()
    {
        var (repo, invoice) = Setup(Invoice.StatusIssued);
        var paidOn = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);
        await new MarkInvoicePaidHandler(repo.Object).Handle(new MarkInvoicePaidCommand(invoice.Id, paidOn), CancellationToken.None);
        Assert.Equal(paidOn, invoice.PaidAtUtc);
    }

    [Fact]
    public async Task Marking_a_voided_invoice_paid_is_refused_with_a_next_step()
    {
        var (repo, invoice) = Setup(Invoice.StatusVoided);
        var ex = await Assert.ThrowsAsync<InvoiceConflictException>(() =>
            new MarkInvoicePaidHandler(repo.Object).Handle(new MarkInvoicePaidCommand(invoice.Id, DateTime.UtcNow), CancellationToken.None));
        Assert.Contains("Issue a new invoice", ex.Message);
    }
}
