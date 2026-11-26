using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace CleaningSuite.Application.Shifts;

public sealed record ShiftOverlapVisit(Guid ShiftId, string ShiftName, DateTime StartUtc, DateTime EndUtc);

public sealed record ShiftOverlapFinding(Guid EmployeeId, ShiftOverlapVisit Earlier, ShiftOverlapVisit Later);

// Finds employees booked on two visits that overlap in time. Back-to-back visits (one ends when the next starts) are not overlaps.
public static class OverlapAudit
{
    public static IReadOnlyList<ShiftOverlapFinding> Find(IEnumerable<(Guid EmployeeId, ShiftOverlapVisit Visit)> visits)
    {
        var findings = new List<ShiftOverlapFinding>();
        foreach (var group in visits.GroupBy(x => x.EmployeeId))
        {
            ShiftOverlapVisit? latest = null;
            foreach (var visit in group.Select(x => x.Visit).OrderBy(x => x.StartUtc).ThenBy(x => x.EndUtc))
            {
                if (latest is not null && visit.StartUtc < latest.EndUtc)
                    findings.Add(new ShiftOverlapFinding(group.Key, latest, visit));
                if (latest is null || visit.EndUtc > latest.EndUtc) latest = visit;
            }
        }
        return findings.OrderBy(f => f.Later.StartUtc).ThenBy(f => f.EmployeeId).ToList();
    }
}

public record ShiftOverlapAuditQuery(DateTime From, DateTime To) : IRequest<IReadOnlyList<ShiftOverlapFinding>>;

public sealed class ShiftOverlapAuditQueryHandler(IShiftRepository repository, ITenantProfileRepository profiles, ITenantContext tenant)
    : IRequestHandler<ShiftOverlapAuditQuery, IReadOnlyList<ShiftOverlapFinding>>
{
    private const int MaxWindowDays = 366;

    public async Task<IReadOnlyList<ShiftOverlapFinding>> Handle(ShiftOverlapAuditQuery request, CancellationToken ct)
    {
        // A missing date arrives as the default value; say which date to choose rather than "end before start".
        if (request.From == default) throw new ValidationException([new ValidationFailure("From", "Choose a start date for the audit window.")]);
        if (request.To == default) throw new ValidationException([new ValidationFailure("To", "Choose an end date for the audit window.")]);
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        // The client sends calendar dates as UTC midnights; the calendar date is the tenant-local day, so no zone conversion is applied.
        var localFrom = request.From.Date;
        var localTo = request.To.Date;
        if (localTo < localFrom)
            throw new ValidationException([new ValidationFailure("To", "The audit end date must be on or after its start date.")]);
        if ((localTo - localFrom).TotalDays > MaxWindowDays)
            throw new ValidationException([new ValidationFailure("To", $"Choose an audit window of {MaxWindowDays} days or fewer.")]);

        var visits = await ScheduleVisits.GatherAsync(repository, zone, localFrom, localTo, ct);
        return OverlapAudit.Find(visits);
    }
}

/// <summary>
/// One employee's visit on the schedule: a dated occurrence of a shift, with what the calendar shows about it.
/// ShiftId with StartUtc (and ExceptionId for a manual visit) identifies the visit, as in the overlap audit.
/// </summary>
public sealed record EmployeeScheduleVisit(Guid ShiftId, string ShiftName, DateTime StartUtc, DateTime EndUtc)
{
    /// <summary>Set for a manual or changed visit; null for a plain scheduled visit.</summary>
    public Guid? ExceptionId { get; init; }
    public string? CompanyName { get; init; }
    public string? BranchName { get; init; }
    /// <summary>The branch address on one line, when the branch has one.</summary>
    public string? Address { get; init; }
    /// <summary>Same work state as the shift's Visits list: Completed, InProgress, Scheduled or NeedsAction.</summary>
    public string? WorkStatus { get; init; }
    public string? VisitNotes { get; init; }
    /// <summary>Number of a property main-shift visit; null for other visits.</summary>
    public int? VisitNumber { get; init; }
    /// <summary>Ticked items on this employee's checklist for the visit. Null when the visit has no checklist yet.</summary>
    public int? ChecklistDone { get; init; }
    public int? ChecklistTotal { get; init; }
}

public sealed record EmployeeDaySchedule(Guid EmployeeId, DateTime LocalDay, IReadOnlyList<EmployeeScheduleVisit> Visits);

public record EmployeeScheduleQuery(DateTime From, DateTime To) : IRequest<IReadOnlyList<EmployeeDaySchedule>>;

// Shared by the overlap audit and the employee-by-day schedule so both see the same visits.
public static class ScheduleVisits
{
    /// <summary>Every rostered (not cancelled) visit of an active shift in the window, once per assigned employee.</summary>
    public static async Task<List<(Guid EmployeeId, Shift Shift, ShiftVisitRosterEntry Entry)>> GatherEntriesAsync(
        IShiftRepository repository, TimeZoneInfo zone, DateTime localFrom, DateTime localTo, CancellationToken ct)
    {
        var fromUtc = TenantTimeZone.StartOfLocalDay(localFrom, zone);
        var toUtc = TenantTimeZone.StartOfLocalDay(localTo.AddDays(1), zone);
        var visits = new List<(Guid EmployeeId, Shift Shift, ShiftVisitRosterEntry Entry)>();
        foreach (var shift in (await repository.ListForReportingAsync(ct)).Where(s => s.IsActive))
        {
            var roster = await repository.ResolveRosterAsync(shift, fromUtc, toUtc, zone, ct);
            foreach (var entry in roster)
                foreach (var employee in entry.EmployeeIds)
                    visits.Add((employee, shift, entry));
        }
        return visits;
    }

    public static async Task<List<(Guid EmployeeId, ShiftOverlapVisit Visit)>> GatherAsync(
        IShiftRepository repository, TimeZoneInfo zone, DateTime localFrom, DateTime localTo, CancellationToken ct) =>
        (await GatherEntriesAsync(repository, zone, localFrom, localTo, ct))
            .Select(x => (x.EmployeeId, new ShiftOverlapVisit(x.Shift.Id, x.Shift.Name, x.Entry.Occurrence.StartUtc, x.Entry.Occurrence.EndUtc)))
            .ToList();

    /// <summary>The checklist counts for one employee's form: ticked items and all items. Null when there is no form or no items.</summary>
    public static (int? Done, int? Total) Checklist(QualityCycleForm? form) =>
        form is null || form.Items.Count == 0 ? (null, null) : (form.Items.Count(i => i.IsChecked), form.Items.Count);

    /// <summary>The branch address on one line, skipping empty parts.</summary>
    public static string? Address(Branch? branch)
    {
        if (branch is null) return null;
        var city = string.Join(" ", new[] { branch.PostalCode, branch.City }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        var parts = new[] { branch.Street?.Trim(), city }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    // Groups visits by employee and tenant-local day, earliest visit first.
    public static IReadOnlyList<EmployeeDaySchedule> GroupByDay(IEnumerable<(Guid EmployeeId, EmployeeScheduleVisit Visit)> visits, TimeZoneInfo zone) =>
        visits.GroupBy(x => (x.EmployeeId, Day: TenantTimeZone.LocalDate(x.Visit.StartUtc, zone)))
            .Select(g => new EmployeeDaySchedule(g.Key.EmployeeId, g.Key.Day, g.Select(x => x.Visit).OrderBy(v => v.StartUtc).ToList()))
            .OrderBy(x => x.LocalDay).ThenBy(x => x.EmployeeId)
            .ToList();
}

public sealed class EmployeeScheduleQueryHandler(IShiftRepository repository, ITenantProfileRepository profiles, ITenantContext tenant,
    IQualityCycleRepository? forms = null, ICompanyRepository? companies = null, IBranchRepository? branches = null, TimeProvider? clock = null)
    : IRequestHandler<EmployeeScheduleQuery, IReadOnlyList<EmployeeDaySchedule>>
{
    public async Task<IReadOnlyList<EmployeeDaySchedule>> Handle(EmployeeScheduleQuery request, CancellationToken ct)
    {
        if (request.From == default) throw new ValidationException([new ValidationFailure("From", "Choose a start date for the schedule.")]);
        if (request.To == default) throw new ValidationException([new ValidationFailure("To", "Choose an end date for the schedule.")]);
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        // The client sends calendar dates as UTC midnights; the calendar date is the tenant-local day, so no zone conversion is applied.
        var localFrom = request.From.Date;
        var localTo = request.To.Date;
        if (localTo < localFrom)
            throw new ValidationException([new ValidationFailure("To", "The schedule end date must be on or after its start date.")]);
        if ((localTo - localFrom).TotalDays > 31)
            throw new ValidationException([new ValidationFailure("To", "Choose a schedule window of 31 days or fewer.")]);
        var entries = await ScheduleVisits.GatherEntriesAsync(repository, zone, localFrom, localTo, ct);
        var windowFrom = TenantTimeZone.StartOfLocalDay(localFrom, zone);
        var windowTo = TenantTimeZone.StartOfLocalDay(localTo.AddDays(1), zone);
        var nowUtc = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        // A moved visit keeps its checklist under its original start, which can fall just outside the window.
        var savedForms = forms is null || entries.Count == 0 ? new List<QualityCycleForm>()
            : (await forms.ListFormsAsync(null, windowFrom.AddDays(-7), windowTo.AddDays(7), ct) ?? []).Where(f => f.ShiftDeletedUtc is null).ToList();
        var companyNames = new Dictionary<Guid, string?>();
        var branchRecords = new Dictionary<Guid, Branch?>();
        var visitRecords = new Dictionary<Guid, Dictionary<Guid, ShiftOccurrenceException>>();
        var visits = new List<(Guid EmployeeId, EmployeeScheduleVisit Visit)>();
        foreach (var (employeeId, shift, entry) in entries)
        {
            if (!companyNames.TryGetValue(shift.CompanyId, out var companyName))
                companyNames[shift.CompanyId] = companyName = companies is null ? null : (await companies.GetAsync(shift.CompanyId, ct))?.Name;
            if (!branchRecords.TryGetValue(shift.BranchId, out var branch))
                branchRecords[shift.BranchId] = branch = branches is null ? null : await branches.GetAsync(shift.BranchId, ct);
            if (!visitRecords.TryGetValue(shift.Id, out var records))
                visitRecords[shift.Id] = records = (await repository.ListOccurrenceExceptionsAsync(shift.Id, windowFrom, windowTo, ct) ?? [])
                    .GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
            // Same matching as the shift's Visits list: a form belongs to the visit's current or original start.
            var occurrenceForms = savedForms.Where(f => f.ShiftId == shift.Id
                && (f.ShiftOccurrenceUtc == entry.Occurrence.StartUtc || f.ShiftOccurrenceUtc == entry.OriginalStartUtc)).ToArray();
            var (done, total) = ScheduleVisits.Checklist(occurrenceForms.Where(f => f.EmployeeId == employeeId)
                .OrderByDescending(f => f.IsSubmitted).ThenByDescending(f => f.UpdatedUtc).FirstOrDefault());
            var record = (entry.ExtraExceptionId ?? entry.Occurrence.ExceptionId) is { } changeId && records.TryGetValue(changeId, out var found) ? found : null;
            visits.Add((employeeId, new EmployeeScheduleVisit(shift.Id, shift.Name, entry.Occurrence.StartUtc, entry.Occurrence.EndUtc)
            {
                ExceptionId = entry.Occurrence.ExceptionId,
                CompanyName = companyName,
                BranchName = branch?.Name,
                Address = ScheduleVisits.Address(branch),
                WorkStatus = WorkQueueRules.OccurrenceWorkStatus(entry, occurrenceForms, nowUtc),
                VisitNotes = record?.Notes,
                VisitNumber = shift.IsBranchMain && entry.ExtraExceptionId is not null ? record?.Sequence : null,
                ChecklistDone = done,
                ChecklistTotal = total,
            }));
        }
        return ScheduleVisits.GroupByDay(visits, zone);
    }
}
