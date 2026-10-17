using CleaningSuite.Domain.Agreements;

namespace CleaningSuite.Application.Agreements;

public interface IAgreementRepository
{
    Task<Agreement?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Agreement>> ListAsync(string tenantId, CancellationToken ct = default);
    Task SaveAsync(string tenantId, Agreement agreement, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<Agreement?> FindBySignerTokenAsync(string tenantId, string token, CancellationToken ct = default);
    Task<Agreement?> ReloadBySignerTokenAsync(string tenantId, string token, CancellationToken ct = default);
    Task<IReadOnlyList<Agreement>> ListMissingCompanyAssociationsAsync(string tenantId, CancellationToken ct = default);
    Task<AgreementAssociationRepairAudit?> GetAssociationRepairAuditAsync(string tenantId, Guid agreementId, CancellationToken ct = default);
    Task SaveAssociationRepairAsync(string tenantId, Agreement agreement, AgreementAssociationRepairAudit audit, CancellationToken ct = default);
}
