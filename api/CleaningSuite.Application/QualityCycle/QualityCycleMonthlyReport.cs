using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.QualityCycle;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.QualityCycle;

public record QualityCycleOccurrenceReport(DateTime StartUtc, DateTime EndUtc, int ExpectedTasks,
    int SubmittedTasks, string Status);
public record QualityCycleEvidenceReport(DateTime StartUtc, DateTime EndUtc, string? Note, IReadOnlyList<string> PhotoUrls)
{
    public IReadOnlyList<QualityCyclePhoto> Photos { get; init; } = Array.Empty<QualityCyclePhoto>();
}
public record QualityCycleShiftReport(Guid ShiftId, string ShiftName, Guid CompanyId, string CompanyName,
    Guid BranchId, string BranchName, int ScheduledOccurrences, decimal ScheduledHours,
    int CompletedOccurrences, int MissingOccurrences, int PendingOccurrences, int TotalTasks,
    int SubmittedTasks, int MissingTasks, int PendingTasks,
    IReadOnlyList<QualityCycleOccurrenceReport> Occurrences, IReadOnlyList<QualityCycleFormDto> Forms,
    IReadOnlyList<QualityCycleItemReport> Items)
{
    public string? BranchAddress { get; init; }
    public IReadOnlyList<QualityCycleWorkTiming>? WorkRecords { get; init; }
    public decimal? RecordedStaffHours { get; init; }
    public int? IncompleteTimingRecords { get; init; }
    public IReadOnlyList<QualityCycleEvidenceReport> Evidence { get; init; } = Array.Empty<QualityCycleEvidenceReport>();
}
public record QualityCycleItemReport(string ItemText, int RecordedOccurrences, int DaysPerformed, IReadOnlyList<string> RecordedDates);
public record QualityCycleMonthlyReport(int Year, int Month, string TimeZoneId, DateTime GeneratedUtc,
    IReadOnlyList<QualityCycleShiftReport> Shifts)
{
    public string Language { get; init; } = "en";
    public IReadOnlyList<QualityCycleReportPeriod>? Periods { get; init; }
}
public record GetQualityCycleMonthlyReportQuery(int Year, int Month, Guid? CompanyId = null,
    Guid? BranchId = null, Guid? ShiftId = null) : IRequest<QualityCycleMonthlyReport>
{
    internal DateOnly? FromDate { get; init; }
    internal DateOnly? ToExclusiveDate { get; init; }
    internal IReadOnlyList<Guid>? ShiftIds { get; init; }
}
public record GetQualityCycleBranchPdfQuery(int Year, int Month, Guid BranchId) : IRequest<byte[]>;

public class MonthlyReportValidator : AbstractValidator<GetQualityCycleMonthlyReportQuery>
{
    public MonthlyReportValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
public class BranchReportValidator : AbstractValidator<GetQualityCycleBranchPdfQuery>
{
    public BranchReportValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.BranchId).NotEmpty();
    }
}
public class ShiftPdfValidator : AbstractValidator<GetQualityCycleSummaryPdfQuery>
{
    public ShiftPdfValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.ShiftId).NotEmpty();
    }
}

public class QualityCycleMonthlyReportService(IShiftRepository shifts, IQualityCycleRepository forms,
    ICompanyRepository companies, IBranchRepository branches, ITenantProfileRepository profiles,
    ITenantContext tenant, TimeProvider clock)
{
    public async Task<QualityCycleMonthlyReport> BuildAsync(GetQualityCycleMonthlyReportQuery request, CancellationToken ct)
    {
        // Validate here too, because PDF handlers call the service directly.
        await new MonthlyReportValidator().ValidateAndThrowAsync(request, ct);
        if (request.CompanyId.HasValue)
            _ = await companies.GetAsync(request.CompanyId.Value, ct) ?? throw new NotFoundException("Company", request.CompanyId.Value);
        if (request.BranchId.HasValue)
        {
            var branch = await branches.GetAsync(request.BranchId.Value, ct) ?? throw new NotFoundException("Branch", request.BranchId.Value);
            if (request.CompanyId.HasValue && branch.CompanyId != request.CompanyId.Value)
                throw new NotFoundException("Branch", request.BranchId.Value);
        }
        var profile = await profiles.GetAsync(tenant.TenantId, ct);
        var zoneId = string.IsNullOrWhiteSpace(profile?.TimeZoneId) ? TenantTimeZone.DefaultId : profile.TimeZoneId;
        var zone = TenantTimeZone.Resolve(zoneId);
        var from = TimeZoneInfo.ConvertTimeToUtc(request.FromDate?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(request.Year, request.Month, 1), zone);
        var to = TimeZoneInfo.ConvertTimeToUtc(request.ToExclusiveDate?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(request.Year, request.Month, 1).AddMonths(1), zone);
        var now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<Shift> candidates = request.ShiftIds is not null
            ? await shifts.ListByIdsAsync(request.ShiftIds, ct)
            : request.ShiftId.HasValue
            ? new[] { await shifts.GetAsync(request.ShiftId.Value, ct) ?? await shifts.GetDeletedSnapshotAsync(request.ShiftId.Value, ct) ?? throw new NotFoundException("Shift", request.ShiftId.Value) }
            : await shifts.ListForReportingAsync(ct);
        if (request.ShiftIds is not null && candidates.Count != request.ShiftIds.Count)
            throw new QualityCycleConflictException("One or more selected shifts no longer exist. Refresh the shift selection.");
        var reports = new List<QualityCycleShiftReport>();
        foreach (var shift in candidates)
        {
            var saved = await forms.ListFormsAsync(shift.Id, from, to, ct);
            var reportingShift = request.ShiftIds is not null ? ShiftContractSchedule.Snapshot(shift, zone) : shift;
            var planned = await QualityCycleOccurrences.ResolvedInWindowAsync(shifts, reportingShift, from, to, zone, ct, includeWithoutChecklist: request.ShiftIds is not null);
            // Submitted historical evidence remains visible even after a shift/template is removed.
            var keys = planned.Select(x => (x.Plan.CompanyId, x.Plan.BranchId))
                .Concat(saved.Where(f => f.IsSubmitted).Select(f => (f.CompanyId == Guid.Empty ? shift.CompanyId : f.CompanyId,
                    f.BranchId == Guid.Empty ? shift.BranchId : f.BranchId))).Distinct().ToList();
            // An explicitly selected shift still needs a summary row when the period has no visits.
            if (request.ShiftIds is not null && keys.Count == 0) keys.Add((shift.CompanyId, shift.BranchId));
            foreach (var (companyId, branchId) in keys)
            {
                if (request.CompanyId.HasValue && companyId != request.CompanyId.Value) continue;
                if (request.BranchId.HasValue && branchId != request.BranchId.Value) continue;
                var company = await companies.GetAsync(companyId, ct);
                var branch = await branches.GetAsync(branchId, ct);
                if (company is null || branch is null || branch.CompanyId != companyId) continue;
                var groupPlans = planned.Where(x => x.Plan.CompanyId == companyId && x.Plan.BranchId == branchId).ToList();
                var groupForms = saved.Where(f => (f.CompanyId == Guid.Empty ? shift.CompanyId : f.CompanyId) == companyId
                    && (f.BranchId == Guid.Empty ? shift.BranchId : f.BranchId) == branchId).ToList();
                var times = groupPlans.Select(x => x.Occurrence.StartUtc)
                    .Concat(groupForms.Where(f => f.IsSubmitted).Select(f => f.ShiftOccurrenceUtc)).Distinct().OrderBy(x => x);
                var rows = new List<QualityCycleOccurrenceReport>();
                var workByVisit = new List<IReadOnlyList<QualityCycleForm>>();
                var missingTasks = 0; var pendingTasks = 0;
                foreach (var time in times)
                {
                    // Stable visits may share a start time with different crews. Keep each
                    // planned visit separate instead of collapsing an extra into the schedule.
                    var expectations = groupPlans.Where(x => x.Occurrence.StartUtc == time).ToList();
                    if (expectations.Count == 0) expectations.Add(default);
                    for (var visitIndex = 0; visitIndex < expectations.Count; visitIndex++)
                    {
                        var expected = expectations[visitIndex];
                        var assignedEmployees = (expected.EmployeeIds ?? Array.Empty<Guid>()).ToHashSet();
                        var matching = groupForms.Where(f => f.ShiftOccurrenceUtc == time
                            && (assignedEmployees.Contains(f.EmployeeId) || (f.IsSubmitted && visitIndex == 0
                                && !expectations.Any(p => p.EmployeeIds is not null && p.EmployeeIds.Contains(f.EmployeeId)))))
                            .GroupBy(f => f.EmployeeId).Select(g => g.OrderByDescending(f => f.IsSubmitted).First()).ToList();
                        var end = expected.Occurrence?.EndUtc ?? matching.Max(f => f.ShiftOccurrenceEndUtc);
                        var employees = assignedEmployees.Concat(matching.Select(f => f.EmployeeId)).Distinct().ToList();
                        var submitted = matching.Count(f => f.IsSubmitted);
                        var remaining = Math.Max(0, employees.Count - submitted);
                        var completed = employees.Count > 0 && remaining == 0;
                        var status = completed ? "Recorded" : end <= now ? "Missing" : "Pending";
                        if (status == "Missing") missingTasks += remaining;
                        if (status == "Pending") pendingTasks += remaining;
                        rows.Add(new(time, end, employees.Count, submitted, status));
                        workByVisit.Add(matching);
                    }
                }
                var displayedForms = groupForms.Where(f => rows.Any(r => r.StartUtc == f.ShiftOccurrenceUtc))
                    .Select(QualityCycleHandlers.MapForm).ToList();
                var name = groupPlans.LastOrDefault().Plan?.ShiftName ?? groupForms.FirstOrDefault()?.ShiftName ?? shift.Name;
                var labels = groupForms.SelectMany(f => f.Items).Select(i => i.ItemText).ToHashSet(StringComparer.Ordinal);
                foreach (var templateId in groupPlans.Select(p => p.Plan.TemplateId).Distinct()) {
                    var template = templateId.HasValue ? await forms.GetTemplateAsync(templateId.Value, ct) : null;
                    if (template is not null) foreach (var item in template.Items) labels.Add(item);
                }
                var itemReports = labels.OrderBy(x => x).Select(label => {
                    var performed = workByVisit.Where(visit => visit.Any(f => f.IsSubmitted && f.Items.Any(i => i.ItemText == label && i.IsChecked)))
                        .Select(visit => visit.First().ShiftOccurrenceUtc).ToList();
                    var dates = performed.Select(time => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(time, DateTimeKind.Utc), zone).ToString("yyyy-MM-dd"))
                        .Distinct().OrderBy(x => x).ToList();
                    return new QualityCycleItemReport(label, performed.Count, dates.Count, dates);
                }).ToList();
                var work = QualityCycleWorkProjection.FromForms(displayedForms);
                reports.Add(new(shift.Id, name, companyId, company.Name, branchId, branch.Name,
                    rows.Count, rows.Sum(r => (decimal)(r.EndUtc - r.StartUtc).TotalHours),
                    rows.Count(r => r.Status == "Recorded"), rows.Count(r => r.Status == "Missing"),
                    rows.Count(r => r.Status == "Pending"), rows.Sum(r => r.ExpectedTasks),
                    rows.Sum(r => r.SubmittedTasks), missingTasks, pendingTasks, rows, displayedForms, itemReports)
                {
                    BranchAddress = string.Join(", ", new[] { branch.Street,
                        string.Join(" ", new[] { branch.PostalCode, branch.City }.Where(x => !string.IsNullOrWhiteSpace(x))), branch.Country }
                        .Where(x => !string.IsNullOrWhiteSpace(x))),
                    WorkRecords = work.Records, RecordedStaffHours = work.StaffHours, IncompleteTimingRecords = work.IncompleteRecords,
                    Evidence = displayedForms.Where(f => f.IsSubmitted &&
                        (!string.IsNullOrWhiteSpace(f.CleanerNotes) || f.PhotoUrls.Count > 0))
                        .OrderBy(f => f.ShiftOccurrenceUtc).ThenBy(f => f.Id)
                        .Select(f => new QualityCycleEvidenceReport(f.ShiftOccurrenceUtc, f.ShiftOccurrenceEndUtc,
                            f.CleanerNotes, f.Photos.Count > 0 ? Array.Empty<string>() : f.PhotoUrls.ToArray()) { Photos = f.Photos.ToArray() }).ToList()
                });
            }
        }
        return new(request.Year, request.Month, zoneId, now,
            reports.OrderBy(r => r.CompanyName).ThenBy(r => r.BranchName).ThenBy(r => r.ShiftName).ToList());
    }
}

public class MonthlyReportHandler(QualityCycleMonthlyReportService reports)
    : IRequestHandler<GetQualityCycleMonthlyReportQuery, QualityCycleMonthlyReport>
{
    public Task<QualityCycleMonthlyReport> Handle(GetQualityCycleMonthlyReportQuery request, CancellationToken ct) => reports.BuildAsync(request, ct);
}
public class BranchPdfHandler(QualityCycleMonthlyReportService reports, IQualityCyclePdfGenerator pdf)
    : IRequestHandler<GetQualityCycleBranchPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetQualityCycleBranchPdfQuery request, CancellationToken ct) =>
        pdf.GenerateMonthlyReportPdf(await reports.BuildAsync(new(request.Year, request.Month, BranchId: request.BranchId), ct));
}
