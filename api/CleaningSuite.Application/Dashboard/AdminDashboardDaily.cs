using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using MediatR;

namespace CleaningSuite.Application.Dashboard;

public sealed record DashboardCompanyDay(Guid CompanyId, string CustomerName, int ScheduledVisits, decimal ScheduledHours);
public sealed record DashboardDay(string Date, int ScheduledVisits, decimal ScheduledHours, decimal ActualHours, IReadOnlyList<DashboardCompanyDay> Companies);
public sealed record DashboardDailyMonth(string Month, IReadOnlyList<DashboardDay> Days);

public record GetAdminDashboardDailyQuery(string Month) : IRequest<DashboardDailyMonth>;

public static class DashboardDailyRules
{
    /// <summary>Per local day of the month: visits, scheduled and actual hours, and each customer's share. Days without data are zero.</summary>
    public static IReadOnlyList<DashboardDay> Build(DateOnly firstDay, int dayCount, IEnumerable<DashboardVisitInput> visits,
        IEnumerable<DashboardWorkInput> work, TimeZoneInfo zone, IReadOnlyDictionary<Guid, string> customerNames)
    {
        string NameOf(Guid companyId) => customerNames.TryGetValue(companyId, out var name) ? name : "Customer";
        string DayOf(DateTime utc) => TenantTimeZone.LocalDate(utc, zone).ToString("yyyy-MM-dd");

        var scheduled = visits.GroupBy(v => DayOf(v.StartUtc)).ToDictionary(g => g.Key, g => g.ToList());
        var done = work.Where(w => w.Submitted).GroupBy(w => DayOf(w.OccurrenceStartUtc)).ToDictionary(g => g.Key, g => g.ToList());

        return Enumerable.Range(0, dayCount).Select(offset =>
        {
            var date = firstDay.AddDays(offset).ToString("yyyy-MM-dd");
            scheduled.TryGetValue(date, out var todayVisits);
            done.TryGetValue(date, out var todayWork);
            todayVisits ??= []; todayWork ??= [];
            var companies = todayVisits.GroupBy(v => v.CompanyId).Select(g => new DashboardCompanyDay(g.Key, NameOf(g.Key), g.Count(),
                Math.Round(g.Sum(v => (decimal)Math.Max(0, (v.EndUtc - v.StartUtc).TotalHours)), 2)))
                .OrderBy(c => c.CustomerName, StringComparer.CurrentCultureIgnoreCase).ToList();
            var actual = todayWork.Sum(w => w.StartedUtc is { } s && w.EndedUtc is { } e && e > s ? (decimal)(e - s).TotalHours : 0m);
            return new DashboardDay(date, todayVisits.Count, Math.Round(companies.Sum(c => c.ScheduledHours), 2), Math.Round(actual, 2), companies);
        }).ToList();
    }
}

public class GetAdminDashboardDailyHandler(IShiftRepository shifts, ICompanyRepository companies, IQualityCycleRepository forms,
    ITenantProfileRepository profiles, ITenantContext tenant)
    : IRequestHandler<GetAdminDashboardDailyQuery, DashboardDailyMonth>
{
    public async Task<DashboardDailyMonth> Handle(GetAdminDashboardDailyQuery request, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(request.Month + "-01", "yyyy-MM-dd", out var firstDay))
            throw new ArgumentException("Use month=yyyy-MM.");
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var nextMonth = firstDay.AddMonths(1);
        var fromUtc = TenantTimeZone.StartOfLocalDay(firstDay.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = TenantTimeZone.StartOfLocalDay(nextMonth.ToDateTime(TimeOnly.MinValue), zone);

        var (roster, shiftCompany) = await DashboardRoster.LoadAsync(shifts, fromUtc, toUtc, zone, ct);
        var visits = roster.Where(v => !v.Cancelled).Select(v => new DashboardVisitInput(v.CompanyId, v.StartUtc, v.EndUtc)).ToList();
        var work = (await forms.ListFormsAsync(null, fromUtc, toUtc, ct) ?? [])
            .Where(f => f.ShiftDeletedUtc is null && shiftCompany.ContainsKey(f.ShiftId))
            .Select(f => new DashboardWorkInput(shiftCompany[f.ShiftId], f.ShiftOccurrenceUtc, f.StartedAtUtc, f.EndedAtUtc, f.IsSubmitted)).ToList();

        var names = new Dictionary<Guid, string>();
        foreach (var companyId in shiftCompany.Values.Distinct())
            names[companyId] = (await companies.GetAsync(companyId, ct))?.Name ?? "Customer";
        var days = DashboardDailyRules.Build(firstDay, firstDay.AddMonths(1).DayNumber - firstDay.DayNumber, visits, work, zone, names);
        return new DashboardDailyMonth(request.Month, days);
    }
}
