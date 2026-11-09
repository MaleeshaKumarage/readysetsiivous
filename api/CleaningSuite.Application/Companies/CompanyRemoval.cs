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
using Microsoft.Extensions.Logging;

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
            var companyInvoices = await partnerStore.Session.Query<Invoice>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
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
                companyInvoices.Count,
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
        IEmployeeIdentityProvider identities, IEmployeeIdentityLock identityLocks, IShiftWorkReservation? reservation = null,
        ILogger<CompanyRemoval.RemoveHandler>? logger = null)
        : IRequestHandler<RemoveCompanyCommand, RemoveCompanyResultDto>
    {
        public async Task<RemoveCompanyResultDto> Handle(RemoveCompanyCommand request, CancellationToken ct)
        {
            // The database writes commit together in RemoveAsync. The identity calls run after that commit, under each
            // account's identity lock, which is the order the employee deactivate uses.
            var (result, loginsToDisable) = await RemoveAsync(request, ct);
            // The removal is already committed. A failed login disable is logged and left pending for a later sync; it must not
            // turn a completed removal into an error the admin would retry.
            foreach (var account in loginsToDisable)
            {
                try { await DisableLoginAsync(account, ct); }
                catch (Exception error) { logger?.LogWarning(error, "Login disable for partner account {AccountId} is pending a later sync.", account.Id); }
            }
            return result;
        }

        // Every write is staged in one session and committed by one SaveChanges at the end. A failure before that commit
        // leaves nothing removed, and a failure during it is rolled back by the database.
        private async Task<(RemoveCompanyResultDto Result, List<CleaningSuite.Domain.Employees.Employee> LoginsToDisable)> RemoveAsync(RemoveCompanyCommand request, CancellationToken ct)
        {
            await using var lease = reservation is null ? null : await reservation.AcquireAsync(ct);
            var company = await companies.GetAsync(request.Id, ct) ?? throw new NotFoundException("Company", request.Id);

            // Refusals are decided before anything is staged.
            if (company.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the company before deleting it.");
            var companyShifts = await shifts.ListAsync(company.Id, null, ct);
            if (request.Branches && !request.Shifts && companyShifts.Count > 0)
                throw new InactiveRecordDeleteConflictException("Places can only be removed together with their shifts. Tick shifts as well.");
            var companyBranches = await branches.ListByCompanyAsync(company.Id, true, ct);
            var branchIds = companyBranches.Select(b => b.Id).ToArray();
            var companyInvoices = await partnerStore.Session.Query<Invoice>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
            var paidCount = companyInvoices.Count(i => i.Status == Invoice.StatusPaid);
            if (request.Invoices && paidCount > 0 && !request.ConfirmPaidInvoices)
                throw new InactiveRecordDeleteConflictException($"This company has {paidCount} paid invoice(s). Confirm their removal to continue.");

            var session = partnerStore.Session;
            var full = request.Branches && request.Shifts && request.Invoices;
            var now = DateTime.UtcNow;
            var shiftsRemoved = 0; var placesRemoved = 0; var invoicesRemoved = 0; var agreementsRemoved = 0;
            var checklistsRemoved = 0; var partnerRemoved = 0; var accountsDeactivated = 0; var templatesUnlinked = 0; var leadsUnlinked = 0;
            var loginsToDisable = new List<CleaningSuite.Domain.Employees.Employee>();

            if (request.Shifts)
            {
                foreach (var shift in companyShifts)
                {
                    if (shift.IsActive)
                    {
                        shift.EnsureQualityCycleHistory();
                        shift.Deactivate();
                        shift.RecordQualityCyclePlan(now);
                        await shifts.SaveWithoutCommitAsync(shift, ct);
                    }
                    foreach (var assignment in await shifts.ListAssignmentsByShiftAsync(shift.Id, ct))
                    {
                        if (!assignment.IsActive) continue;
                        assignment.EndFrom(now, now);
                        await shifts.SaveAssignmentWithoutCommitAsync(assignment, ct);
                    }
                    await shifts.DeleteWithoutCommitAsync(shift.Id, ct);
                    shiftsRemoved++;
                }
            }

            if (request.Branches)
            {
                // Partner requests and notifications for these places go with them.
                var placeRequests = await session.Query<PartnerCleaningRequest>().Where(x => branchIds.Contains(x.BranchId)).ToListAsync(ct);
                var placeChanges = await session.Query<PartnerChangeRequest>().Where(x => branchIds.Contains(x.BranchId)).ToListAsync(ct);
                var placeNotes = await session.Query<PartnerNotification>().Where(x => branchIds.Contains(x.BranchId)).ToListAsync(ct);
                foreach (var item in placeRequests) session.Delete(item);
                foreach (var item in placeChanges) session.Delete(item);
                foreach (var item in placeNotes) session.Delete(item);
                partnerRemoved += placeRequests.Count + placeChanges.Count + placeNotes.Count;
                foreach (var branch in companyBranches) { session.Delete<Branch>(branch.Id); placesRemoved++; }
            }

            if (request.Invoices)
            {
                foreach (var invoice in companyInvoices) session.Delete<Invoice>(invoice.Id);
                invoicesRemoved = companyInvoices.Count;
            }

            if (full)
            {
                foreach (var agreement in (await agreements.ListAsync(tenant.TenantId, ct)).Where(a => a.CompanyId == company.Id))
                {
                    session.Delete<Agreement>(agreement.Id);
                    agreementsRemoved++;
                }
                var audits = await session.Query<AgreementAssociationRepairAudit>().Where(x => x.CompanyId == company.Id || x.PreviousCompanyId == company.Id).ToListAsync(ct);
                foreach (var audit in audits) session.Delete(audit);

                var checklists = await session.Query<QualityCycleForm>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var form in checklists) session.Delete(form);
                checklistsRemoved = checklists.Count;

                var companyRequests = await session.Query<PartnerCleaningRequest>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                var companyChanges = await session.Query<PartnerChangeRequest>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var item in companyRequests) session.Delete(item);
                foreach (var item in companyChanges) session.Delete(item);
                partnerRemoved += companyRequests.Count + companyChanges.Count;

                // Partner accounts are deactivated and unlinked from the company. Their login is disabled after the removal commits.
                var accounts = await session.Query<CleaningSuite.Domain.Employees.Employee>().Where(x => x.PartnerCompanyId == company.Id).ToListAsync(ct);
                foreach (var account in accounts)
                {
                    account.PartnerCompanyId = null;
                    account.IsActive = false;
                    account.IdentitySyncPending = !string.IsNullOrWhiteSpace(account.KeycloakUserId);
                    account.UpdatedUtc = DateTime.UtcNow;
                    session.Store(account);
                    if (account.IdentitySyncPending) loginsToDisable.Add(account);
                }
                accountsDeactivated = accounts.Count;

                // Templates and leads are kept and unlinked from the company.
                var templates = await session.Query<QualityCycleTemplate>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var template in templates) { template.CompanyId = null; session.Store(template); }
                templatesUnlinked = templates.Count;
                var leads = await session.Query<Lead>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
                foreach (var lead in leads) { lead.CompanyId = null; session.Store(lead); }
                leadsUnlinked = leads.Count;

                session.Delete<Company>(company.Id);
            }

            // The one commit runs to completion even if the client has gone: a half-finished commit is the failure this avoids.
            await session.SaveChangesAsync(CancellationToken.None);
            return (new RemoveCompanyResultDto(shiftsRemoved, placesRemoved, invoicesRemoved, agreementsRemoved, checklistsRemoved,
                partnerRemoved, accountsDeactivated, templatesUnlinked, leadsUnlinked, full), loginsToDisable);
        }

        private async Task DisableLoginAsync(CleaningSuite.Domain.Employees.Employee account, CancellationToken ct)
        {
            var tenantId = tenant.TenantId;
            await using var identityLease = await identityLocks.AcquireAsync(tenantId, account.Email, ct);
            await identities.SynchronizeAccessAsync(tenantId, account.Id, account.KeycloakUserId!, account.Role, false, ct);
            // Reload after the provider call, then clear the pending flag. A failure here leaves the flag set for a later sync.
            // Eject the tracked instance first, so the reload reads the stored state and not the copy this session just saved.
            partnerStore.Session.Eject(account);
            var current = await partnerStore.Session.LoadAsync<CleaningSuite.Domain.Employees.Employee>(account.Id, ct);
            if (current is null || current.KeycloakUserId != account.KeycloakUserId || current.IsActive) return;
            current.IdentitySyncPending = false;
            partnerStore.Session.Store(current);
            await partnerStore.Session.SaveChangesAsync(ct);
        }
    }
}
