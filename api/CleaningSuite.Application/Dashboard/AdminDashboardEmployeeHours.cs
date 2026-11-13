using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using MediatR;

namespace CleaningSuite.Application.Dashboard;

public sealed record DashboardCompanyHours(Guid CompanyId, string CustomerName, decimal ScheduledHours);
public sealed record DashboardEmployeeHours(Guid EmployeeId, string Name, decimal ScheduledHours, IReadOnlyList<DashboardCompanyHours> Companies);
public sealed record DashboardEmployeeWeek(string WeekStart, decimal FullTimeHours, IReadOnlyList<DashboardEmployeeHours> Employees);

public record GetAdminDashboardEmployeeHoursQuery(DateOnly? WeekStart) : IRequest<DashboardEmployeeWeek>;

public static class DashboardEmployeeHoursRules
{
    /// <summary>The weekly hours of a full-time cleaner. A donut shows scheduled hours against this.</summary>
    public const decimal FullTimeWeeklyHours = 37.5m;

    /// <summary>
    /// Scheduled hours per employee, split by customer: each visit's length, counted once for every employee on it.
    /// A cancelled visit adds nothing. Every given employee is listed, including those with no visits.
    /// </summary>
    public static IReadOnlyList<DashboardEmployeeHours> Build(
        IEnumerable<(Guid CompanyId, IReadOnlyList<Guid> EmployeeIds, DateTime StartUtc, DateTime EndUtc, bool Cancelled)> visits,
        IEnumerable<(Guid Id, string Name)> employees, IReadOnlyDictionary<Guid, string> customerNames)
    {
        var hours = new Dictionary<Guid, Dictionary<Guid, decimal>>();
        foreach (var visit in visits)
        {
            if (visit.Cancelled) continue;
            var length = (decimal)(visit.EndUtc - visit.StartUtc).TotalHours;
            if (length <= 0) continue;
            foreach (var id in visit.EmployeeIds.Distinct())
            {
                if (!hours.TryGetValue(id, out var byCompany)) hours[id] = byCompany = new Dictionary<Guid, decimal>();
                byCompany[visit.CompanyId] = byCompany.GetValueOrDefault(visit.CompanyId) + length;
            }
        }
        return employees
            .Select(e =>
            {
                var byCompany = hours.GetValueOrDefault(e.Id) ?? new Dictionary<Guid, decimal>();
                var companies = byCompany
                    .Select(c => new DashboardCompanyHours(c.Key, customerNames.TryGetValue(c.Key, out var name) ? name : "Customer", Math.Round(c.Value, 2)))
                    .OrderBy(c => c.CustomerName, StringComparer.CurrentCultureIgnoreCase).ToList();
                return new DashboardEmployeeHours(e.Id, e.Name, Math.Round(companies.Sum(c => c.ScheduledHours), 2), companies);
            })
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

public class GetAdminDashboardEmployeeHoursHandler(IShiftRepository shifts, IEmployeeRepository employees, ICompanyRepository companies,
    ITenantProfileRepository profiles, ITenantContext tenant, TimeProvider? clock = null)
    : IRequestHandler<GetAdminDashboardEmployeeHoursQuery, DashboardEmployeeWeek>
{
    public async Task<DashboardEmployeeWeek> Handle(GetAdminDashboardEmployeeHoursQuery request, CancellationToken ct)
    {
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var today = TenantTimeZone.LocalDate((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, zone);
        // A week runs Monday to Sunday in the tenant's time zone; any day given is moved back to its Monday.
        var anchor = request.WeekStart ?? DateOnly.FromDateTime(today);
        var monday = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
        var fromUtc = TenantTimeZone.StartOfLocalDay(monday.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = TenantTimeZone.StartOfLocalDay(monday.AddDays(7).ToDateTime(TimeOnly.MinValue), zone);

        var (roster, _) = await DashboardRoster.LoadAsync(shifts, fromUtc, toUtc, zone, ct);
        var names = new Dictionary<Guid, string>();
        foreach (var companyId in roster.Select(v => v.CompanyId).Distinct())
            names[companyId] = (await companies.GetAsync(companyId, ct))?.Name ?? "Customer";
        var visits = roster.Select(v => (v.CompanyId, v.EmployeeIds, v.StartUtc, v.EndUtc, v.Cancelled));

        var people = (await employees.ListAsync(includeInactive: false, ct))
            .Where(e => e.Role != Employee.RolePartner)
            .Select(e => (e.Id, Name: $"{e.FirstName} {e.LastName}".Trim()));
        return new DashboardEmployeeWeek(monday.ToString("yyyy-MM-dd"), DashboardEmployeeHoursRules.FullTimeWeeklyHours,
            DashboardEmployeeHoursRules.Build(visits, people, names));
    }
}
