using CleaningSuite.Domain.Partners;
namespace CleaningSuite.Application.Invoicing;

public interface IPartnerInvoiceSource
{
    Task<IReadOnlyList<PartnerCleaningRequest>> ListCompanyAsync(Guid companyId, CancellationToken ct);
}
