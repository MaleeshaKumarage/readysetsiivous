using CleaningSuite.Domain.Invoicing;
using MediatR;

namespace CleaningSuite.Application.Invoicing.Queries;

public record ListInvoicesQuery(string? Status, DateTime? FromUtc) : IRequest<IReadOnlyList<Invoice>>;

public class ListInvoicesHandler : IRequestHandler<ListInvoicesQuery, IReadOnlyList<Invoice>>
{
    private readonly IInvoiceRepository _invoices;

    public ListInvoicesHandler(IInvoiceRepository invoices) => _invoices = invoices;

    public Task<IReadOnlyList<Invoice>> Handle(ListInvoicesQuery request, CancellationToken ct) =>
        _invoices.ListAsync(request.Status, request.FromUtc, ct);
}

public record GetInvoiceQuery(Guid Id) : IRequest<Invoice?>;

public class GetInvoiceHandler : IRequestHandler<GetInvoiceQuery, Invoice?>
{
    private readonly IInvoiceRepository _invoices;

    public GetInvoiceHandler(IInvoiceRepository invoices) => _invoices = invoices;

    public Task<Invoice?> Handle(GetInvoiceQuery request, CancellationToken ct) =>
        _invoices.GetByIdAsync(request.Id, ct);
}
