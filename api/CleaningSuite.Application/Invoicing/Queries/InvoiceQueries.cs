using CleaningSuite.Domain.Invoicing;
using MediatR;

namespace CleaningSuite.Application.Invoicing.Queries;

public record ListInvoicesQuery(string? Status, DateTime? FromUtc, Guid? CompanyId = null) : IRequest<IReadOnlyList<Invoice>>;

public class ListInvoicesHandler : IRequestHandler<ListInvoicesQuery, IReadOnlyList<Invoice>>
{
    private readonly IInvoiceRepository _invoices;

    public ListInvoicesHandler(IInvoiceRepository invoices) => _invoices = invoices;

    public async Task<IReadOnlyList<Invoice>> Handle(ListInvoicesQuery request, CancellationToken ct)
    {
        var invoices = await _invoices.ListAsync(request.Status, request.FromUtc, ct);
        // shortcut: company filter is applied in memory after the tenant list; upgrade to a repository filter if invoice volume grows.
        return request.CompanyId is Guid companyId ? invoices.Where(i => i.CompanyId == companyId).ToList() : invoices;
    }
}

public record GetInvoiceQuery(Guid Id) : IRequest<Invoice?>;

public class GetInvoiceHandler : IRequestHandler<GetInvoiceQuery, Invoice?>
{
    private readonly IInvoiceRepository _invoices;

    public GetInvoiceHandler(IInvoiceRepository invoices) => _invoices = invoices;

    public Task<Invoice?> Handle(GetInvoiceQuery request, CancellationToken ct) =>
        _invoices.GetByIdAsync(request.Id, ct);
}
