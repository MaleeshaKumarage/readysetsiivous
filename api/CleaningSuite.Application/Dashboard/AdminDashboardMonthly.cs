using CleaningSuite.Application.Shifts;

namespace CleaningSuite.Application.Dashboard;

public sealed record DashboardVisitInput(Guid CompanyId, DateTime StartUtc, DateTime EndUtc);
public sealed record DashboardWorkInput(Guid CompanyId, DateTime OccurrenceStartUtc, DateTime? StartedUtc, DateTime? EndedUtc, bool Submitted);
public sealed record DashboardCustomerMonth(Guid CompanyId, string CustomerName, int ScheduledVisits, int CompletedVisits, decimal ScheduledHours, decimal ActualHours);
public sealed record DashboardMonth(string Month, IReadOnlyList<DashboardCustomerMonth> Customers);

/// <summary>
/// Counts per calendar month (tenant time zone) and customer: scheduled visits and their scheduled hours, and completed
/// visits with the actual hours cleaners submitted. A submitted record with a missing or reversed time is not counted as hours.
/// </summary>
public static class DashboardMonthlyRules
{
    public static IReadOnlyList<DashboardMonth> Build(IEnumerable<DashboardVisitInput> visits, IEnumerable<DashboardWorkInput> work,
        TimeZoneInfo zone, IReadOnlyDictionary<Guid, string> customerNames)
    {
        string MonthOf(DateTime utc) => TenantTimeZone.LocalDate(utc, zone).ToString("yyyy-MM");
        string NameOf(Guid companyId) => customerNames.TryGetValue(companyId, out var name) ? name : "Customer";

        var scheduled = visits.GroupBy(v => (Month: MonthOf(v.StartUtc), v.CompanyId)).ToDictionary(g => g.Key,
            g => (Count: g.Count(), Hours: g.Sum(v => (decimal)Math.Max(0, (v.EndUtc - v.StartUtc).TotalHours))));
        var done = work.Where(w => w.Submitted).GroupBy(w => (Month: MonthOf(w.OccurrenceStartUtc), w.CompanyId)).ToDictionary(g => g.Key,
            g => (Count: g.Count(), Hours: g.Sum(w => w.StartedUtc is { } s && w.EndedUtc is { } e && e > s ? (decimal)(e - s).TotalHours : 0m)));

        return scheduled.Keys.Concat(done.Keys).Distinct().GroupBy(k => k.Month).OrderBy(g => g.Key).Select(month => new DashboardMonth(month.Key,
            month.Select(k =>
            {
                scheduled.TryGetValue(k, out var s); done.TryGetValue(k, out var d);
                return new DashboardCustomerMonth(k.CompanyId, NameOf(k.CompanyId), s.Count, d.Count,
                    Math.Round(s.Hours, 2), Math.Round(d.Hours, 2));
            }).OrderBy(c => c.CustomerName, StringComparer.CurrentCultureIgnoreCase).ToList())).ToList();
    }
}
