using System;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Invoicing.Commands;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Invoicing;

public class InvoiceNotFoundTests
{
    [Fact]
    public async Task Marking_an_unknown_invoice_paid_says_to_refresh_without_exposing_the_id()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IInvoiceRepository>();
        repo.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((CleaningSuite.Domain.Invoicing.Invoice?)null);
        var ex = await Assert.ThrowsAsync<InvoiceNotFoundException>(() =>
            new MarkInvoicePaidHandler(repo.Object).Handle(new MarkInvoicePaidCommand(id, DateTime.UtcNow), default));
        Assert.Contains("Refresh the invoice list", ex.Message);
        Assert.DoesNotContain(id.ToString(), ex.Message);
    }
}
