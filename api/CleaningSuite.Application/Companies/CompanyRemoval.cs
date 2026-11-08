using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Marten;
using MediatR;

namespace CleaningSuite.Application.Companies;

// Counts shown in the removal dialog before anything is removed.
public record CompanyRemovalSummaryDto(int Branches, int Shifts, int ShiftsWithChecklists, int Invoices, int PaidInvoices,
    int Agreements, int ChecklistSubmissions, int PartnerRequests, int PartnerAccounts, int Templates, int Leads);

public record RemoveCompanySummaryQuery(Guid Id) : IRequest<CompanyRemovalSummaryDto>;

// Each ticked category is removed. Places need their shifts removed too. Removing all three also removes the company
// record and everything linked to it: agreements, checklists, partner records. Partner accounts are deactivated and their
// login is disabled, the same as deactivating an employee; templates and leads are kept and unlinked.
public record RemoveCompanyCommand(Guid Id, bool Branches, bool Shifts, bool Invoices, bool ConfirmPaidInvoices = false)
    : IRequest<RemoveCompanyResultDto>;

public record RemoveCompanyResultDto(int ShiftsRemoved, int PlacesRemoved, int InvoicesRemoved, int AgreementsRemoved,
    int ChecklistsRemoved, int PartnerRecordsRemoved, int PartnerAccountsDeactivated, int TemplatesUnlinked, int LeadsUnlinked, bool CompanyRemoved);

public static class CompanyRemoval
{
    public sealed class SummaryHandler(ICompanyRepository companies, IBranchRepository branches, IShiftRepository shifts,
        IInvoiceRepository invoices, IAgreementRepository agreements, IQualityCycleRepository forms, ITenantContext tenant,
        IPartnerStore partnerStore) : IRequestHandler<RemoveCompanySummaryQuery, CompanyRemovalSummaryDto>
    {
        public async Task<CompanyRemovalSummaryDto> Handle(RemoveCompanySummaryQuery request, CancellationToken ct)
        {
            var company = await companies.GetAsync(request.Id, ct) ?? throw new NotFoundException("Company", request.Id);
            var session = partnerStore.Session;
            var companyBranches = await branches.ListByCompanyAsync(company.Id, true, ct);
            var branchIds = companyBranches.Select(b => b.Id).ToArray();
            var companyShifts = await shifts.ListAsync(company.Id, null, ct);
            var shiftIds = companyShifts.Select(s => s.Id).ToArray();
            var companyInvoices = (await invoices.ListAsync(null, null, ct)).Where(i => i.CompanyId == company.Id).ToArray();
            var companyAgreements = (await agreements.ListAsync(tenant.TenantId, ct)).Where(a => a.CompanyId == company.Id).ToArray();
            var checklists = await forms.ListFormsAsync(null, null, null, ct, null, null, company.Id, null);
            var partnerRequests = await session.Query<PartnerCleaningRequest>().Where(x => x.CompanyId == company.Id || branchIds.Contains(x.BranchId)).ToListAsync(ct);
            var partnerChanges = await session.Query<PartnerChangeRequest>().Where(x => x.CompanyId == company.Id || branchIds.Contains(x.BranchId)).ToListAsync(ct);
            var partnerAccounts = await session.Query<CleaningSuite.Domain.Employees.Employee>().Where(x => x.PartnerCompanyId == company.Id).ToListAsync(ct);
            var templates = await session.Query<QualityCycleTemplate>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
            var leads = await session.Query<Lead>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
            return new CompanyRemovalSummaryDto(
                companyBranches.Count,
                companyShifts.Count,
                shiftIds.Length == 0 ? 0 : (await forms.ListShiftIdsWithSubmittedFormAsync(shiftIds, ct)).Count,
                companyInvoices.Length,
                companyInvoices.Count(i => i.Status == Invoice.StatusPaid),
                companyAgreements.Length,
                checklists.Count,
                partnerRequests.Count + partnerChanges.Count,
                partnerAccounts.Count,
                templates.Count,
                leads.Count);
        }
    }

    public sealed class RemoveHandler(ICompanyRepository companies, IBranchRepository branches, IShiftRepository shifts,
        IInvoiceRepository invoices, IAgreementRepository agreements, ITenantContext tenant, IPartnerStore partnerStore,
        IEmployeeIdentityProvider identities, IEmployeeIdentityLock identityLocks, IShiftWorkReservation? reservation = null)
        : IRequestHandler<RemoveCompanyCommand, RemoveCompanyResultDto>
    {
        public async Task<RemoveCompanyResultDto> Handle(RemoveCompanyCommand request, CancellationToken ct)
        {
            // The removal runs under the work reservation. The identity calls run after it is released, under each
            // account's identity lock, which is the order the employee deactivate uses.
            var (result, loginsToDisable) = await RemoveAsync(request, ct);
            foreach (var account in loginsToDisable) await DisableLoginAsync(account, ct);
            return result;
        }

        private async Task<(RemoveCompanyResultDto Result, List<CleaningSuite.Domain.Employees.Employee> LoginsToDisable)> RemoveAsync(RemoveCompanyCommand request, CancellationToken ct)
        {
            await using var lease = reservation is null ? null : await reservation.AcquireAsync(ct);
            var company = await companies.GetAsync(request.Id, ct) ?? throw new NotFoundException("Company", request.Id);

            // Refusals are decided before anything is removed.
            if (company.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the company before deleting it.");
            var companyShifts = await shifts.ListAsync(company.Id, null, ct);
            if (request.Branches && !request.Shifts && companyShifts.Count > 0)
                throw new InactiveRecordDeleteConflictException("Places can only be removed together with their shifts. Tick shifts as well.");
            var companyBranches = await branches.ListByCompanyAsync(company.Id, true, ct);
            var branchIds = companyBranches.Select(b => b.Id).ToArray();
            var companyInvoices = (await invoices.ListAsync(null, null, ct)).Where(i => i.CompanyId == company.Id).ToArray();
            var paidCount = companyInvoices.Count(i => i.Status == Invoice.StatusPaid);
            if (request.Invoices && paidCount > 0 && !request.ConfirmPaidInvoices)
                throw new InactiveRecordDeleteConflictException($"This company has {paidCount} paid invoice(s). Confirm their removal to continue.");

            var full = request.Branches && request.Shifts && request.Invoices;
            var now = DateTime.UtcNow;
            var shiftsRemoved = 0; var placesRemoved = 0; var invoicesRemoved = 0; var agreementsRemoved = 0;
            var checklistsRemoved = 0; var partnerRemoved = 0; var accountsDeactivated = 0; var templatesUnlinked = 0; var leadsUnlinked = 0;
            var loginsToDisable = new List<CleaningSuite.Domain.Employees.Employee>();

            // Each save commits on its own, so the removal is not one transaction. Refusals are checked above; a failure part way
            // leaves the finished categories removed and the rest in place, and the same removal can be run again.
            if (request.Shifts)
            {
                foreach (var shift in companyShifts)
                {
                    if (shift.IsActive)
                    {
                        shift.EnsureQualityCycleHistory();
                        shift.Deactivate();
                        shift.RecordQualityCyclePlan(now);
                        await shifts.SaveAsync(shift, ct);
                    }
                    foreach (var assignment in await shifts.ListAssignmentsByShiftAsync(shift.Id, ct))
                    {
                        if (!assignment.IsActive) continue;
                        assignment.EndFrom(now, now);
                        await shifts.SaveAssignmentAsync(assignment, ct);
                    }
                    await shifts.DeleteAsync(shift.Id, ct);
                    shiftsRemoved++;
                }
            }

            if (request.Branches)
            {
                // Partner requests and notifications for these places go with them.
                var placeRequests = await partnerStore.Session.Query<PartnerCleaningRequest>().Where(x => branchIds.Contains(x.BranchId)).ToListAsync(ct);
                var placeChanges = await partnerStore.Session.Query<PartnerChangeRequest>().Where(x => branchIds.Contains(x.BranchId)).ToListAsync(ct);
                var placeNotes = await partnerStore.Session.Query<PartnerNotification>().Where(x => branchIds.Contains(x.BranchId)).ToListAsync(ct);
                foreach (var item in placeRequests) partnerStore.Session.Delete(item);
                foreach (var item in placeChanges) partnerStore.Session.Delete(item);
                foreach (var item in placeNotes) partnerStore.Session.Delete(item);
                partnerRemoved += placeRequests.Count + placeChanges.Count + placeNotes.Count;
                await partnerStore.Session.SaveChangesAsync(ct);

                foreach (var branch in companyBranches)
                {
                    if (branch.IsActive)
                    {
                        branch.Deactivate();
                        await branches.SaveAsync(branch, ct);
                    }
                    await branches.DeleteAsync(branch.Id, ct);
                    placesRemoved++;
                }
            }

            if (request.Invoices)
            {
                foreach (var invoice in companyInvoices) partnerStore.Session.Delete<Invoice>(invoice.Id);
                invoicesRemoved = companyInvoices.Length;
                await partnerStore.Session.SaveChangesAsync(ct);
            }

            if (full)
            {
                foreach (var agreement in (await agreements.ListAsync(tenant.TenantId, ct)).Where(a => a.CompanyId == company.Id))
                {
                    await agreements.DeleteAsync(tenant.TenantId, agreement.Id, ct);
                    agreementsRemoved++;
                }
                var audits = await partnerStore.Session.Query<AgreementAssociationRepairAudit>().Where(x => x.CompanyId == company.Id || x.PreviousCompanyId == company.Id).ToListAsync(ct);
                foreach (var audit in audits) partnerStore.Session.Delete(audit);

                var checklists = await partnerStore.Session.Query<QualityCycleForm>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var form in checklists) partnerStore.Session.Delete(form);
                checklistsRemoved = checklists.Count;

                var companyRequests = await partnerStore.Session.Query<PartnerCleaningRequest>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                var companyChanges = await partnerStore.Session.Query<PartnerChangeRequest>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var item in companyRequests) partnerStore.Session.Delete(item);
                foreach (var item in companyChanges) partnerStore.Session.Delete(item);
                partnerRemoved += companyRequests.Count + companyChanges.Count;

                // Partner accounts are deactivated and unlinked from the company. Their login is disabled after the removal commits.
                var accounts = await partnerStore.Session.Query<CleaningSuite.Domain.Employees.Employee>().Where(x => x.PartnerCompanyId == company.Id).ToListAsync(ct);
                foreach (var account in accounts)
                {
                    account.PartnerCompanyId = null;
                    account.IsActive = false;
                    account.IdentitySyncPending = !string.IsNullOrWhiteSpace(account.KeycloakUserId);
                    account.UpdatedUtc = DateTime.UtcNow;
                    partnerStore.Session.Store(account);
                    if (account.IdentitySyncPending) loginsToDisable.Add(account);
                }
                accountsDeactivated = accounts.Count;

                // Templates and leads are kept and unlinked from the company.
                var templates = await partnerStore.Session.Query<QualityCycleTemplate>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var template in templates) { template.CompanyId = null; partnerStore.Session.Store(template); }
                templatesUnlinked = templates.Count;
                var leads = await partnerStore.Session.Query<Lead>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var lead in leads) { lead.CompanyId = null; partnerStore.Session.Store(lead); }
                leadsUnlinked = leads.Count;

                await partnerStore.Session.SaveChangesAsync(ct);
                await companies.DeleteAsync(company.Id, ct);
            }

            return (new RemoveCompanyResultDto(shiftsRemoved, placesRemoved, invoicesRemoved, agreementsRemoved, checklistsRemoved,
                partnerRemoved, accountsDeactivated, templatesUnlinked, leadsUnlinked, full), loginsToDisable);
        }

        private async Task DisableLoginAsync(CleaningSuite.Domain.Employees.Employee account, CancellationToken ct)
        {
            var tenantId = tenant.TenantId;
            await using var identityLease = await identityLocks.AcquireAsync(tenantId, account.Email, ct);
            await identities.SynchronizeAccessAsync(tenantId, account.Id, account.KeycloakUserId!, account.Role, false, ct);
            // Reload after the provider call, then clear the pending flag. A failure here leaves the flag set for a later sync.
            var current = await partnerStore.Session.LoadAsync<CleaningSuite.Domain.Employees.Employee>(account.Id, ct);
            if (current is null || current.KeycloakUserId != account.KeycloakUserId || current.IsActive) return;
            current.IdentitySyncPending = false;
            partnerStore.Session.Store(current);
            await partnerStore.Session.SaveChangesAsync(ct);
        }
    }
}
