using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public sealed record LegacyAgreementRepairCandidate(Guid AgreementId, string Title, string Status,
    Guid MissingCompanyId, Guid ExpectedVersion, DateTime CreatedUtc);
public sealed record LegacyAgreementRepairPreview(IReadOnlyList<LegacyAgreementRepairCandidate> Agreements,
    IReadOnlyList<CompanyDto> Companies);
public sealed record RepairLegacyAgreementAssociationCommand(Guid AgreementId, Guid CompanyId,
    Guid ExpectedVersion, string ActorId) : IRequest<LegacyAgreementRepairCandidate>;
public sealed record PreviewLegacyAgreementRepairsQuery : IRequest<LegacyAgreementRepairPreview>;

public sealed class PreviewLegacyAgreementRepairsHandler : IRequestHandler<PreviewLegacyAgreementRepairsQuery, LegacyAgreementRepairPreview>
{
    private readonly ITenantContext _tenant;
    private readonly IAgreementRepository _agreements;
    private readonly ICompanyRepository _companies;

    public PreviewLegacyAgreementRepairsHandler(ITenantContext tenant, IAgreementRepository agreements, ICompanyRepository companies)
    { _tenant = tenant; _agreements = agreements; _companies = companies; }

    public async Task<LegacyAgreementRepairPreview> Handle(PreviewLegacyAgreementRepairsQuery request, CancellationToken ct)
    {
        const int pageSize = 200;
        var companyItems = new List<CleaningSuite.Domain.Companies.Company>();
        var totalCompanies = 0;
        do
        {
            var page = await _companies.ListAsync(null, companyItems.Count, pageSize, ct);
            totalCompanies = page.Total;
            companyItems.AddRange(page.Items);
            if (page.Items.Count == 0) break;
        } while (companyItems.Count < totalCompanies);
        var companiesById = companyItems.ToDictionary(x => x.Id);
        var candidates = new List<LegacyAgreementRepairCandidate>();
        foreach (var agreement in await _agreements.ListMissingCompanyAssociationsAsync(_tenant.TenantId, ct))
        {
            if (!IsEligible(agreement) || companiesById.ContainsKey(agreement.CompanyId)) continue;
            candidates.Add(new(agreement.Id, agreement.Title, agreement.Status, agreement.CompanyId, agreement.Version, agreement.CreatedUtc));
        }

        return new(candidates.OrderBy(x => x.CreatedUtc).ToArray(), companyItems
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CompanyDto(x.Id, x.BusinessId, x.Name, x.ContactName, x.ContactEmail, x.ContactPhone, x.Notes, x.IsActive))
            .ToArray());
    }

    internal static bool IsEligible(Agreement agreement) =>
        agreement.Status is Agreement.StatusDraft or Agreement.StatusCancelled
        && agreement.Signers.All(s => s.Status != Signer.StatusSigned
            && s.SignatureImagePath is null
            && s.TypedName is null
            && s.SignedAtUtc is null);
}

public sealed class RepairLegacyAgreementAssociationHandler : IRequestHandler<RepairLegacyAgreementAssociationCommand, LegacyAgreementRepairCandidate>
{
    private readonly ITenantContext _tenant;
    private readonly IAgreementRepository _agreements;
    private readonly ICompanyRepository _companies;

    public RepairLegacyAgreementAssociationHandler(ITenantContext tenant, IAgreementRepository agreements, ICompanyRepository companies)
    { _tenant = tenant; _agreements = agreements; _companies = companies; }

    public async Task<LegacyAgreementRepairCandidate> Handle(RepairLegacyAgreementAssociationCommand request, CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;
        var existingAudit = await _agreements.GetAssociationRepairAuditAsync(tenantId, request.AgreementId, ct);
        if (existingAudit is not null)
        {
            if (existingAudit.CompanyId == request.CompanyId)
            {
                var repaired = await _agreements.GetAsync(tenantId, request.AgreementId, ct)
                    ?? throw new NotFoundException("Agreement", request.AgreementId);
                if (repaired.CompanyId != existingAudit.CompanyId)
                    throw new AgreementConflictException("The agreement company link changed after its legacy repair.");
                return Candidate(repaired, existingAudit.PreviousCompanyId);
            }
            throw new AgreementConflictException("This agreement was already repaired to a different company.");
        }

        var agreement = await _agreements.GetAsync(tenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);
        if (agreement.Version != request.ExpectedVersion)
            throw new AgreementConflictException("The agreement changed after preview. Refresh and review it again.");
        if (!PreviewLegacyAgreementRepairsHandler.IsEligible(agreement))
            throw new AgreementConflictException("Only unsigned draft or cancelled agreements can be repaired.");
        if (await _companies.GetAsync(agreement.CompanyId, ct) is not null)
            throw new AgreementConflictException("The agreement already has a valid company association.");
        var selectedCompany = await _companies.GetAsync(request.CompanyId, ct)
            ?? throw new NotFoundException("Company", request.CompanyId);
        if (string.IsNullOrWhiteSpace(request.ActorId))
            throw new FluentValidation.ValidationException("A user identity is required for the audit record.");

        var missingCompanyId = agreement.CompanyId;
        agreement.CompanyId = selectedCompany.Id;
        agreement.UpdatedUtc = DateTime.UtcNow;
        var audit = new AgreementAssociationRepairAudit
        {
            Id = agreement.Id,
            AgreementId = agreement.Id,
            PreviousCompanyId = missingCompanyId,
            CompanyId = selectedCompany.Id,
            ActorId = request.ActorId,
            RepairedUtc = DateTime.UtcNow,
        };
        await _agreements.SaveAssociationRepairAsync(tenantId, agreement, audit, ct);
        return Candidate(agreement, missingCompanyId);
    }

    private static LegacyAgreementRepairCandidate Candidate(Agreement agreement, Guid missingCompanyId) =>
        new(agreement.Id, agreement.Title, agreement.Status, missingCompanyId, agreement.Version, agreement.CreatedUtc);
}
