using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Partners;
using Marten;
namespace CleaningSuite.Infrastructure.Partners;

public sealed class PartnerInvoiceSource(IPartnerStore store) : IPartnerInvoiceSource
{
    public async Task<IReadOnlyList<PartnerCleaningRequest>> ListCompanyAsync(Guid companyId, CancellationToken ct) =>
        await store.Session.Query<PartnerCleaningRequest>().Where(r => r.CompanyId == companyId && r.LinkedShiftId != null).ToListAsync(ct);
}
