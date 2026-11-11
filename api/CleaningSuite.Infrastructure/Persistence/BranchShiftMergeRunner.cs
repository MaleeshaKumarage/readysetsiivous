using System.Text.Json;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// Folds partner bookings that still own a shift into their branch's main shift, for every active tenant.
/// Mode "Report" logs the plan and changes nothing. Mode "Apply" performs it: each shift is one commit, made under
/// the tenant work reservation, with a backup record of everything it changed. A failure leaves that shift untouched
/// and it is tried again on the next start. Merged shifts no longer exist, so a repeat run finds nothing to redo.
/// </summary>
public sealed class BranchShiftMergeRunner(IDocumentStore store, ITenantRegistry registry, IConfiguration configuration, ILogger<BranchShiftMergeRunner> logger)
{
    public sealed record Result(string Tenant, int Merged, int Removed, int Kept, int Failed, bool Applied);

    public async Task<IReadOnlyList<Result>> RunAsync(string mode, CancellationToken ct)
    {
        var apply = string.Equals(mode, "Apply", StringComparison.OrdinalIgnoreCase);
        var results = new List<Result>();
        foreach (var registration in await registry.ListAsync(ct))
        {
            if (registration.Status != TenantRegistration.StatusActive) continue;
            results.Add(await RunTenantAsync(registration.Slug, apply, ct));
        }
        return results;
    }

    public async Task<Result> RunTenantAsync(string tenant, bool apply, CancellationToken ct)
    {
        var profile = await new TenantProfileRepository(store).GetAsync(tenant, ct);
        // The same default the rest of the application uses when a tenant has no profile yet.
        var zone = TenantTimeZone.Resolve(string.IsNullOrWhiteSpace(profile?.TimeZoneId) ? TenantTimeZone.DefaultId : profile.TimeZoneId);
        IReadOnlyList<BranchShiftMergeFinding> findings;
        using (var reader = store.QuerySession(tenant))
        {
            var shifts = (await reader.Query<Shift>().ToListAsync(ct)).Where(x => x.IsBranchMain || x.PartnerRequestId.HasValue || x.Schedule.IsOneTime).ToList();
            if (!shifts.Any(x => !x.IsBranchMain)) return new(tenant, 0, 0, 0, 0, apply);
            var ids = shifts.Select(x => x.Id).ToArray();
            findings = BranchShiftMerge.Plan(shifts,
                await reader.Query<PartnerCleaningRequest>().Where(x => x.LinkedShiftId != null).ToListAsync(ct),
                await reader.Query<ShiftOccurrenceException>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct),
                await reader.Query<ShiftOccurrenceAssignment>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct),
                await reader.Query<QualityCycleForm>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct),
                await reader.Query<Invoice>().Where(x => x.Status == Invoice.StatusIssued || x.Status == Invoice.StatusPaid).ToListAsync(ct),
                await reader.Query<Branch>().ToListAsync(ct),
                await reader.Query<ShiftAssignment>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct),
                await reader.Query<ShiftBillingProfile>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct), zone);
        }
        foreach (var finding in findings)
            logger.LogInformation("Branch shift merge {Mode} tenant {Tenant} {Kind} shift {ShiftId} branch {BranchId} {Action}: {Reason}",
                apply ? "apply" : "report", tenant, finding.Kind, finding.ShiftId, finding.BranchId, finding.Action, finding.Reason);

        int merged = 0, removed = 0, failed = 0;
        var kept = findings.Count(x => x.Action == BranchShiftMergeAction.Keep);
        if (apply)
        {
            foreach (var finding in findings.Where(x => x.Action != BranchShiftMergeAction.Keep))
            {
                try
                {
                    if (await ApplyAsync(tenant, finding, zone, ct)) { if (finding.Action == BranchShiftMergeAction.Merge) merged++; else removed++; }
                    else kept++;
                }
                catch (Exception error) when (!ct.IsCancellationRequested)
                {
                    failed++;
                    logger.LogError(error, "Branch shift merge tenant {Tenant} shift {ShiftId} failed and was left unchanged; it is retried on the next start.", tenant, finding.ShiftId);
                }
            }
        }
        logger.LogInformation("Branch shift merge tenant {Tenant}: {Merge} to merge, {Remove} to remove, {Kept} kept for review, {Failed} failed, mode {Mode}",
            tenant, apply ? merged : findings.Count(x => x.Action == BranchShiftMergeAction.Merge),
            apply ? removed : findings.Count(x => x.Action == BranchShiftMergeAction.Remove), kept, failed, apply ? "Apply" : "Report");
        return new(tenant, merged, removed, kept, failed, apply);
    }

    /// <summary>One shift, one commit. Everything is read again under the reservation, so the plan is checked against current data.</summary>
    private async Task<bool> ApplyAsync(string tenant, BranchShiftMergeFinding planned, TimeZoneInfo zone, CancellationToken ct)
    {
        using var factory = new TenantSessionFactory(store, new FixedTenant(tenant));
        await using var lease = await new ShiftWorkReservation(configuration, factory).AcquireAsync(ct);
        var session = factory.Session;
        var shift = await session.LoadAsync<Shift>(planned.ShiftId, ct);
        if (shift is null) return false;
        var related = (await session.Query<Shift>().Where(x => x.BranchId == shift.BranchId).ToListAsync(ct)).Where(x => x.Id == shift.Id || x.IsBranchMain).ToList();
        var ids = related.Select(x => x.Id).ToArray();
        var requests = await session.Query<PartnerCleaningRequest>().Where(x => x.LinkedShiftId == shift.Id).ToListAsync(ct);
        var exceptions = await session.Query<ShiftOccurrenceException>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct);
        var forms = await session.Query<QualityCycleForm>().Where(x => x.ShiftId == shift.Id).ToListAsync(ct);
        var branch = await session.LoadAsync<Branch>(shift.BranchId, ct);
        var assignments = await session.Query<ShiftAssignment>().Where(x => x.ShiftId == shift.Id).ToListAsync(ct);
        var billing = await session.Query<ShiftBillingProfile>().Where(x => x.ShiftId == shift.Id).ToListAsync(ct);
        var current = BranchShiftMerge.Plan(related, requests, exceptions,
            await session.Query<ShiftOccurrenceAssignment>().Where(x => x.ShiftId == shift.Id).ToListAsync(ct), forms,
            await session.Query<Invoice>().Where(x => x.Status == Invoice.StatusIssued || x.Status == Invoice.StatusPaid).ToListAsync(ct),
            branch is null ? [] : [branch], assignments, billing, zone).SingleOrDefault(x => x.ShiftId == shift.Id);
        if (current is null || current.Action != planned.Action)
        {
            logger.LogInformation("Branch shift merge tenant {Tenant} shift {ShiftId} changed since it was planned and was kept: {Reason}", tenant, planned.ShiftId, current?.Reason ?? "no longer a single-visit shift");
            return false;
        }
        var oneTime = current.Kind == BranchShiftMerge.KindOneTime;
        var request = oneTime ? null : requests.Single(x => x.Id == shift.PartnerRequestId);
        var visit = oneTime ? null : exceptions.SingleOrDefault(x => x.ShiftId == shift.Id);
        var visitStart = current.VisitStartUtc;
        var moved = current.Action == BranchShiftMergeAction.Merge ? forms.Where(f => f.ShiftOccurrenceUtc == visitStart).ToList() : [];
        var record = new BranchShiftMergeRecord { Id = shift.Id, BranchId = shift.BranchId, PartnerRequestId = request?.Id, Reason = current.Reason,
            CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow, AppliedUtc = DateTime.UtcNow,
            // Taken before anything is changed.
            BackupJson = JsonSerializer.Serialize(new { Shift = shift, Visit = visit, Request = request, Assignments = assignments, Billing = billing, WorkRecords = moved }) };

        if (current.Action == BranchShiftMergeAction.Merge)
        {
            var localDate = TenantTimeZone.LocalDate(visitStart!.Value, zone);
            var main = await BranchMainShifts.FindOrCreateAsync(session, branch!, localDate, ct);
            if (main.QualityCycleTemplateId is null && shift.QualityCycleTemplateId is Guid template)
            {
                main.EnsureQualityCycleHistory(); main.QualityCycleTemplateId = template; main.RecordQualityCyclePlan(DateTime.UtcNow);
            }
            main.CoverVisitDate(localDate);
            if (oneTime)
            {
                // The one-time shift's generated visit becomes a stored visit on the main shift, held by the same cleaners.
                visit = ShiftOccurrenceException.Create(main.Id, null, visitStart.Value, current.VisitEndUtc!.Value, ShiftOccurrenceExceptionStatus.Extra,
                    "Merged from a one-time shift", current.EmployeeIds.Count == 1 ? current.EmployeeIds[0] : null);
                visit.Id = BranchShiftMerge.OneTimeVisitId(shift.Id);
                visit.Sequence = main.NextOccurrenceSequence(); visit.Source = ShiftOccurrenceException.SourceAdminPanel;
                visit.Notes = shift.Notes; visit.RequiredPhotoLabels = shift.RequiredPhotoLabels.Count > 0 ? shift.RequiredPhotoLabels.ToList() : null;
                visit.LegacyShiftId = shift.Id;
                if (current.EmployeeIds.Count > 1)
                    session.Store(ShiftOccurrenceAssignment.Create(main.Id, null, visit.Id, OccurrenceAssignmentMode.Replace, current.EmployeeIds, "Merged from a one-time shift"));
                if (current.KeptPrice is { } keptPrice)
                {
                    // The visit keeps the price it was agreed at, as a visit price on the main shift.
                    var mainBilling = await session.Query<ShiftBillingProfile>().FirstOrDefaultAsync(x => x.ShiftId == main.Id, ct)
                        ?? new ShiftBillingProfile { Id = main.Id, ShiftId = main.Id, CreatedUtc = DateTime.UtcNow };
                    mainBilling.Overrides.RemoveAll(x => x.OccurrenceStartUtc == keptPrice.OccurrenceStartUtc);
                    mainBilling.Overrides.Add(keptPrice); mainBilling.UpdatedUtc = DateTime.UtcNow;
                    session.Store(mainBilling);
                }
            }
            else
            {
                main.PartnerAccessInstructions ??= shift.PartnerAccessInstructions;
                main.PartnerContactName ??= shift.PartnerContactName;
                main.PartnerContactPhone ??= shift.PartnerContactPhone;
                request!.OccurrenceSequence ??= main.NextOccurrenceSequence();
                visit!.ShiftId = main.Id; visit.LegacyShiftId = shift.Id; visit.PartnerRequestId = request.Id;
                visit.Sequence = request.OccurrenceSequence; visit.Source = ShiftOccurrenceException.SourcePartnerPortal;
                visit.Notes = shift.Notes; visit.RequiredPhotoLabels = shift.RequiredPhotoLabels.ToList();
                visit.EmployeeId = request.AcceptedEmployeeId ?? visit.EmployeeId; visit.UpdatedUtc = DateTime.UtcNow;
                request.LinkedShiftId = main.Id; request.OccurrenceStartUtc = visit.StartUtc;
            }
            foreach (var form in moved) { form.OriginalShiftId = shift.Id; form.ShiftId = main.Id; session.Store(form); }
            record.MainShiftId = main.Id; record.VisitNumber = visit.Sequence; record.Outcome = BranchShiftMergeRecord.OutcomeMerged;
            await ShiftTabs.StoreAsync(session, main, ct); session.Store(visit);
        }
        else
        {
            if (visit is not null) session.Delete(visit);
            if (request is not null) request.LinkedShiftId = null;
            record.Outcome = BranchShiftMergeRecord.OutcomeRemoved;
        }
        if (request is not null) session.Store(request);
        foreach (var assignment in assignments) session.Delete(assignment);
        foreach (var profile in billing) session.Delete(profile);
        session.Delete(shift);
        session.Store(record);
        await session.SaveChangesAsync(ct);
        logger.LogInformation("Branch shift merge tenant {Tenant} shift {ShiftId} {Outcome} into {MainShiftId} as visit {VisitNumber}", tenant, shift.Id, record.Outcome, record.MainShiftId, record.VisitNumber);
        return true;
    }

    private sealed class FixedTenant(string tenant) : ITenantContext
    {
        public string TenantId => tenant;
    }
}
