using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.QualityCycle;
using FluentValidation;
using MediatR;
using TimeZoneConverter;

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
    public IReadOnlyList<QualityCycleEvidenceReport> Evidence { get; init; } = Array.Empty<QualityCycleEvidenceReport>();
}
public record QualityCycleItemReport(string ItemText, int RecordedOccurrences, int DaysPerformed, IReadOnlyList<string> RecordedDates);
public record QualityCycleMonthlyReport(int Year, int Month, string TimeZoneId, DateTime GeneratedUtc,
    IReadOnlyList<QualityCycleShiftReport> Shifts);
public record GetQualityCycleMonthlyReportQuery(int Year, int Month, Guid? CompanyId = null,
    Guid? BranchId = null, Guid? ShiftId = null) : IRequest<QualityCycleMonthlyReport>;
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
        var zoneId = profile?.TimeZoneId ?? "Europe/Helsinki";
        var zone = TZConvert.GetTimeZoneInfo(zoneId);
        var from = TimeZoneInfo.ConvertTimeToUtc(new DateTime(request.Year, request.Month, 1), zone);
        var to = TimeZoneInfo.ConvertTimeToUtc(new DateTime(request.Year, request.Month, 1).AddMonths(1), zone);
        var now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<Shift> candidates = request.ShiftId.HasValue
            ? new[] { await shifts.GetAsync(request.ShiftId.Value, ct) ?? throw new NotFoundException("Shift", request.ShiftId.Value) }
            : await shifts.ListForReportingAsync(ct);
        var reports = new List<QualityCycleShiftReport>();
        foreach (var shift in candidates)
        {
            var saved = await forms.ListFormsAsync(shift.Id, from, to, ct);
            var history = await shifts.ListAssignmentHistoryAsync(shift.Id, ct);
            var planned = QualityCycleOccurrences.InWindow(shift, from, to);
            // Submitted historical evidence remains visible even after a shift/template is removed.
            var keys = planned.Select(x => (x.Plan.CompanyId, x.Plan.BranchId))
                .Concat(saved.Where(f => f.IsSubmitted).Select(f => (f.CompanyId == Guid.Empty ? shift.CompanyId : f.CompanyId,
                    f.BranchId == Guid.Empty ? shift.BranchId : f.BranchId))).Distinct().ToList();
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
                var missingTasks = 0; var pendingTasks = 0;
                foreach (var time in times)
                {
                    var expected = groupPlans.FirstOrDefault(x => x.Occurrence.StartUtc == time);
                    var assignedEmployees = history.Where(a => a.Overlaps(time, expected.Occurrence?.EndUtc
                        ?? groupForms.Where(f => f.ShiftOccurrenceUtc == time).Max(f => f.ShiftOccurrenceEndUtc))).Select(a => a.EmployeeId).ToHashSet();
                    var matching = groupForms.Where(f => f.ShiftOccurrenceUtc == time && (f.IsSubmitted || assignedEmployees.Contains(f.EmployeeId)))
                        .GroupBy(f => f.EmployeeId).Select(g => g.OrderByDescending(f => f.IsSubmitted).First()).ToList();
                    var end = expected.Occurrence?.EndUtc ?? matching.Max(f => f.ShiftOccurrenceEndUtc);
                    var employees = assignedEmployees
                        .Concat(matching.Select(f => f.EmployeeId)).Distinct().ToList();
                    var submitted = matching.Count(f => f.IsSubmitted);
                    var remaining = Math.Max(0, employees.Count - submitted);
                    var completed = employees.Count > 0 && remaining == 0;
                    var status = completed ? "Recorded"
                        : end <= now ? "Missing" : "Pending";
                    if (status == "Missing") missingTasks += remaining;
                    if (status == "Pending") pendingTasks += remaining;
                    rows.Add(new(time, end, employees.Count, submitted, status));
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
                    var performed = groupForms.Where(f => f.IsSubmitted && f.Items.Any(i => i.ItemText == label && i.IsChecked))
                        .Select(f => f.ShiftOccurrenceUtc).Distinct().ToList();
                    var dates = performed.Select(time => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(time, DateTimeKind.Utc), zone).ToString("yyyy-MM-dd"))
                        .Distinct().OrderBy(x => x).ToList();
                    return new QualityCycleItemReport(label, performed.Count, dates.Count, dates);
                }).ToList();
                reports.Add(new(shift.Id, name, companyId, company.Name, branchId, branch.Name,
                    rows.Count, rows.Sum(r => (decimal)(r.EndUtc - r.StartUtc).TotalHours),
                    rows.Count(r => r.Status == "Recorded"), rows.Count(r => r.Status == "Missing"),
                    rows.Count(r => r.Status == "Pending"), rows.Sum(r => r.ExpectedTasks),
                    rows.Sum(r => r.SubmittedTasks), missingTasks, pendingTasks, rows, displayedForms, itemReports)
                {
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
