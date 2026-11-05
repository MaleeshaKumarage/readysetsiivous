using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Invoicing.Commands;
using CleaningSuite.Domain.Invoicing;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Invoicing;

public class InvoicePaymentPolicyTests
{
    private static (Mock<IInvoiceRepository> repository, Invoice invoice) ArrangeInvoice(string status)
    {
        var invoice = new Invoice { Id = Guid.NewGuid(), Status = status };
        var repository = new Mock<IInvoiceRepository>();
        repository.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return (repository, invoice);
    }

    [Fact]
    public async Task Marking_an_issued_invoice_paid_saves_the_payment()
    {
        var (repository, invoice) = ArrangeInvoice(Invoice.StatusIssued);
        var paidAt = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

        await new MarkInvoicePaidHandler(repository.Object).Handle(new MarkInvoicePaidCommand(invoice.Id, paidAt), CancellationToken.None);

        Assert.Equal(Invoice.StatusPaid, invoice.Status);
        Assert.Equal(paidAt, invoice.PaidAtUtc);
        repository.Verify(r => r.SaveAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Marking_a_paid_invoice_paid_again_is_rejected_and_keeps_the_first_payment()
    {
        var (repository, invoice) = ArrangeInvoice(Invoice.StatusPaid);
        var originalPaidAt = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        invoice.PaidAtUtc = originalPaidAt;

        await Assert.ThrowsAsync<InvoiceConflictException>(() =>
            new MarkInvoicePaidHandler(repository.Object).Handle(new MarkInvoicePaidCommand(invoice.Id, DateTime.UtcNow), CancellationToken.None));

        Assert.Equal(originalPaidAt, invoice.PaidAtUtc);
        repository.Verify(r => r.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Marking_a_voided_invoice_paid_is_rejected()
    {
        var (repository, invoice) = ArrangeInvoice(Invoice.StatusVoided);

        await Assert.ThrowsAsync<InvoiceConflictException>(() =>
            new MarkInvoicePaidHandler(repository.Object).Handle(new MarkInvoicePaidCommand(invoice.Id, DateTime.UtcNow), CancellationToken.None));

        Assert.Equal(Invoice.StatusVoided, invoice.Status);
        repository.Verify(r => r.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Voiding_a_paid_invoice_is_rejected_and_the_payment_stays()
    {
        var (repository, invoice) = ArrangeInvoice(Invoice.StatusPaid);

        await Assert.ThrowsAsync<InvoiceConflictException>(() =>
            new VoidInvoiceHandler(repository.Object).Handle(new VoidInvoiceCommand(invoice.Id), CancellationToken.None));

        Assert.Equal(Invoice.StatusPaid, invoice.Status);
        repository.Verify(r => r.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Voiding_an_issued_invoice_is_allowed()
    {
        var (repository, invoice) = ArrangeInvoice(Invoice.StatusIssued);

        await new VoidInvoiceHandler(repository.Object).Handle(new VoidInvoiceCommand(invoice.Id), CancellationToken.None);

        Assert.Equal(Invoice.StatusVoided, invoice.Status);
        repository.Verify(r => r.SaveAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
    }
}
