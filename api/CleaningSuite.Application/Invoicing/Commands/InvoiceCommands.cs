using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Invoicing;
using MediatR;

namespace CleaningSuite.Application.Invoicing.Commands;

public record CreateInvoiceCommand(Guid BookingId) : IRequest<Guid>;

public record MarkInvoicePaidCommand(Guid Id, DateTime PaidAtUtc) : IRequest<Unit>;

public record VoidInvoiceCommand(Guid Id) : IRequest<Unit>;

public class CreateInvoiceHandler : IRequestHandler<CreateInvoiceCommand, Guid>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IBookingRepository _bookings;
    private readonly ITenantProfileRepository _profiles;
    private readonly ITenantContext _context;

    public CreateInvoiceHandler(
        IInvoiceRepository invoices,
        IBookingRepository bookings,
        ITenantProfileRepository profiles,
        ITenantContext context)
    {
        _invoices = invoices;
        _bookings = bookings;
        _profiles = profiles;
        _context = context;
    }

    public async Task<Guid> Handle(CreateInvoiceCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, ct)
            ?? throw new NotFoundException("Booking", request.BookingId);

        if (booking.Status != Booking.StatusCompleted)
            throw new CleaningSuite.Application.Bookings.SlotConflictException(
                "Only completed bookings can be invoiced");

        var open = await _invoices.FindOpenForBookingAsync(request.BookingId, ct);
        if (open.Count > 0)
            throw new CleaningSuite.Application.Bookings.SlotConflictException(
                $"Booking already has invoice {open[0].InvoiceNumber}");

        var profile = await _profiles.GetAsync(_context.TenantId, ct)
            ?? throw new NotFoundException("TenantProfile", Guid.Empty);

        var number = await _invoices.NextInvoiceNumberAsync(ct);
        var issueDate = DateTime.UtcNow;
        var dueDate = issueDate.AddDays(profile.PaymentTermsDays);

        var line = new InvoiceLine
        {
            Description = booking.Service.NameFi,
            Quantity = 1,
            UnitPriceNet = booking.Service.PriceNet,
            VatRatePercent = booking.Service.VatRatePercent,
            VatAmount = decimal.Round(
                booking.Service.PriceNet * booking.Service.VatRatePercent / 100m, 2),
        };

        var invoice = new Invoice
        {
            InvoiceNumber = $"{issueDate.Year}-{number:D4}",
            BookingId = booking.Id,
            BookingNumber = booking.BookingNumber,
            Customer = booking.Customer,
            Issuer = new IssuerInfo
            {
                CompanyName = profile.CompanyName,
                BusinessId = profile.BusinessId,
                CompanyAddress = profile.CompanyAddress,
                Email = profile.Email,
                Phone = profile.Phone,
                BankAccountIBAN = profile.BankAccountIBAN,
                BankBic = profile.BankBic,
            },
            Lines = [line],
            Total = new CleaningSuite.Domain.Common.Money
            {
                Net = line.UnitPriceNet,
                Vat = line.VatAmount,
            },
            Currency = "EUR",
            IssueDate = issueDate,
            DueDate = dueDate,
            Status = Invoice.StatusIssued,
        };

        await _invoices.SaveAsync(invoice, ct);
        return invoice.Id;
    }
}

public class MarkInvoicePaidHandler : IRequestHandler<MarkInvoicePaidCommand, Unit>
{
    private readonly IInvoiceRepository _invoices;

    public MarkInvoicePaidHandler(IInvoiceRepository invoices) => _invoices = invoices;

    public async Task<Unit> Handle(MarkInvoicePaidCommand request, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Invoice", request.Id);

        if (invoice.Status == Invoice.StatusVoided)
            throw new CleaningSuite.Application.Bookings.SlotConflictException("Invoice is voided");

        invoice.Status = Invoice.StatusPaid;
        invoice.PaidAtUtc = request.PaidAtUtc;
        invoice.UpdatedUtc = DateTime.UtcNow;
        await _invoices.SaveAsync(invoice, ct);
        return Unit.Value;
    }
}

public class VoidInvoiceHandler : IRequestHandler<VoidInvoiceCommand, Unit>
{
    private readonly IInvoiceRepository _invoices;

    public VoidInvoiceHandler(IInvoiceRepository invoices) => _invoices = invoices;

    public async Task<Unit> Handle(VoidInvoiceCommand request, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Invoice", request.Id);

        invoice.Status = Invoice.StatusVoided;
        invoice.UpdatedUtc = DateTime.UtcNow;
        await _invoices.SaveAsync(invoice, ct);
        return Unit.Value;
    }
}
