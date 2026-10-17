using System.Globalization;
using CleaningSuite.Application.Companies;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.QualityCycle;

public record QualityCycleReportPeriod(DateOnly From, DateOnly To);
public record QualityCycleReportSelection(IReadOnlyList<Guid> ShiftIds, IReadOnlyList<string>? Months = null,
    DateOnly? From = null, DateOnly? To = null, string Language = "en")
{
    public IReadOnlyList<QualityCycleReportPeriod> ValidatePeriods()
    {
        void Invalid(string reason) => throw new ValidationException([new FluentValidation.Results.ValidationFailure("Selection", reason)]);
        if (ShiftIds is null || ShiftIds.Count == 0 || ShiftIds.Count > 50 || ShiftIds.Any(id => id == Guid.Empty))
            Invalid("Select between 1 and 50 shifts.");
        if (Language is not ("en" or "fi" or "sv")) Invalid("Select English, Finnish or Swedish.");
        var periods = new List<QualityCycleReportPeriod>();
        if (Months is { Count: > 0 })
        {
            if (From.HasValue || To.HasValue || Months.Count > 12) Invalid("Choose up to 12 months OR one date range.");
            foreach (var month in Months.Distinct().Order())
            {
                if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                    || start.Year is < 2000 or > 2100) Invalid("Months must use YYYY-MM between 2000 and 2100.");
                periods.Add(new(start, start.AddMonths(1).AddDays(-1)));
            }
        }
        else
        {
            if (!From.HasValue || !To.HasValue || From > To || From.Value.Year < 2000 || To.Value.Year > 2100)
                Invalid("Enter a valid From–To date range between 2000 and 2100.");
            periods.Add(new(From!.Value, To!.Value));
        }
        if (periods.Sum(p => p.To.DayNumber - p.From.DayNumber + 1) > 366) Invalid("Select at most 366 calendar days.");
        return periods;
    }
}
public record GetSelectedQualityCycleReportQuery(QualityCycleReportSelection Selection) : IRequest<QualityCycleMonthlyReport>;
public record GetSelectedQualityCycleReportPdfQuery(QualityCycleReportSelection Selection) : IRequest<byte[]>;
public record QualityCycleRecipientOption(string Label, string Email);
public record GetQualityCycleRecipientOptionsQuery(IReadOnlyList<Guid> ShiftIds) : IRequest<IReadOnlyList<QualityCycleRecipientOption>>;

public class QualityCycleSelectedReportService(QualityCycleMonthlyReportService reports)
{
    public async Task<QualityCycleMonthlyReport> BuildAsync(QualityCycleReportSelection selection, CancellationToken ct)
    {
        var periods = selection.ValidatePeriods();
        var parts = new List<QualityCycleMonthlyReport>();
        foreach (var period in periods)
            parts.Add(await reports.BuildAsync(new(period.From.Year, period.From.Month) {
                FromDate = period.From, ToExclusiveDate = period.To.AddDays(1), ShiftIds = selection.ShiftIds.Distinct().ToArray()
            }, ct));
        var combined = parts.SelectMany(p => p.Shifts).GroupBy(s => (s.ShiftId, s.CompanyId, s.BranchId)).Select(group => {
            var first = group.First();
            var forms = group.SelectMany(s => s.Forms).DistinctBy(f => f.Id).ToArray();
            var work = QualityCycleWorkProjection.FromForms(forms);
            var items = group.SelectMany(s => s.Items).GroupBy(i => i.ItemText).Select(g => {
                var dates = g.SelectMany(i => i.RecordedDates).Distinct().Order().ToArray();
                return new QualityCycleItemReport(g.Key, g.Sum(i => i.RecordedOccurrences), dates.Length, dates);
            }).OrderBy(i => i.ItemText).ToArray();
            return first with {
                ScheduledOccurrences = group.Sum(s => s.ScheduledOccurrences), ScheduledHours = group.Sum(s => s.ScheduledHours),
                CompletedOccurrences = group.Sum(s => s.CompletedOccurrences), MissingOccurrences = group.Sum(s => s.MissingOccurrences),
                PendingOccurrences = group.Sum(s => s.PendingOccurrences), TotalTasks = group.Sum(s => s.TotalTasks),
                SubmittedTasks = group.Sum(s => s.SubmittedTasks), MissingTasks = group.Sum(s => s.MissingTasks), PendingTasks = group.Sum(s => s.PendingTasks),
                Occurrences = group.SelectMany(s => s.Occurrences).OrderBy(o => o.StartUtc).ToArray(), Forms = forms, Items = items,
                WorkRecords = work.Records, RecordedStaffHours = work.StaffHours, IncompleteTimingRecords = work.IncompleteRecords,
                Evidence = group.SelectMany(s => s.Evidence).OrderBy(e => e.StartUtc).ToArray()
            };
        }).OrderBy(s => s.CompanyName).ThenBy(s => s.BranchName).ThenBy(s => s.ShiftName).ToArray();
        return parts[0] with { Shifts = combined, Periods = periods, Language = selection.Language };
    }
}
public class SelectedReportHandler(QualityCycleSelectedReportService reports) : IRequestHandler<GetSelectedQualityCycleReportQuery, QualityCycleMonthlyReport>
{
    public Task<QualityCycleMonthlyReport> Handle(GetSelectedQualityCycleReportQuery request, CancellationToken ct) => reports.BuildAsync(request.Selection, ct);
}
public class SelectedReportPdfHandler(QualityCycleSelectedReportService reports, IQualityCyclePdfGenerator pdf) : IRequestHandler<GetSelectedQualityCycleReportPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetSelectedQualityCycleReportPdfQuery request, CancellationToken ct) => pdf.GenerateMonthlyReportPdf(await reports.BuildAsync(request.Selection, ct));
}
public class RecipientOptionsHandler(CleaningSuite.Application.Shifts.IShiftRepository shifts, ICompanyRepository companies, IBranchRepository branches)
    : IRequestHandler<GetQualityCycleRecipientOptionsQuery, IReadOnlyList<QualityCycleRecipientOption>>
{
    public async Task<IReadOnlyList<QualityCycleRecipientOption>> Handle(GetQualityCycleRecipientOptionsQuery request, CancellationToken ct)
    {
        new QualityCycleReportSelection(request.ShiftIds, new[] { "2026-01" }).ValidatePeriods();
        var options = new List<QualityCycleRecipientOption>();
        foreach (var shift in await shifts.ListByIdsAsync(request.ShiftIds.Distinct().ToArray(), ct))
        {
            var company = await companies.GetAsync(shift.CompanyId, ct);
            var branch = await branches.GetAsync(shift.BranchId, ct);
            if (!string.IsNullOrWhiteSpace(company?.ContactEmail)) options.Add(new($"Company: {company.Name}", company.ContactEmail));
            if (!string.IsNullOrWhiteSpace(branch?.ContactEmail)) options.Add(new($"Branch: {branch.Name}", branch.ContactEmail));
        }
        return options.DistinctBy(o => o.Email.ToLowerInvariant()).ToArray();
    }
}
