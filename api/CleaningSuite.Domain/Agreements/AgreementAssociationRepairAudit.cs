using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Agreements;

/// <summary>Immutable audit entry for the one-time repair of a legacy agreement company link.</summary>
public sealed class AgreementAssociationRepairAudit : BaseDocument
{
    public Guid AgreementId { get; set; }
    public Guid PreviousCompanyId { get; set; }
    public Guid CompanyId { get; set; }
    public string ActorId { get; set; } = "";
    public DateTime RepairedUtc { get; set; }
}
