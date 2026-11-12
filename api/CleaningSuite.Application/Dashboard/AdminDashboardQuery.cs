using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using MediatR;

namespace CleaningSuite.Application.Dashboard;

public record GetAdminDashboardMonthlyQuery(int Months) : IRequest<IReadOnlyList<DashboardMonth>>;

public class GetAdminDashboardMonthlyHandler(IShiftRepository shifts, ICompanyRepository companies, IQualityCycleRepository forms,
    ITenantProfileRepository profiles, ITenantContext tenant, TimeProvider? clock = null)
    : IRequestHandler<GetAdminDashboardMonthlyQuery, IReadOnlyList<DashboardMonth>>
{
    public async Task<IReadOnlyList<DashboardMonth>> Handle(GetAdminDashboardMonthlyQuery request, CancellationToken ct)
    {
        var months = Math.Clamp(request.Months, 1, 12);
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var today = TenantTimeZone.LocalDate((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, zone);
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var nextAfter = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var fromUtc = TenantTimeZone.StartOfLocalDay(firstMonth.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = TenantTimeZone.StartOfLocalDay(nextAfter.ToDateTime(TimeOnly.MinValue), zone);

        var visits = new List<DashboardVisitInput>();
        var shiftCompany = new Dictionary<Guid, Guid>();
        foreach (var shift in await shifts.ListAllAsync(ct))
        {
            shiftCompany[shift.Id] = shift.CompanyId;
            foreach (var entry in await shifts.ResolveRosterAsync(shift, fromUtc, toUtc, zone, ct))
                if (!entry.IsCancelled) visits.Add(new(shift.CompanyId, entry.Occurrence.StartUtc, entry.Occurrence.EndUtc));
        }
        var work = (await forms.ListFormsAsync(null, fromUtc, toUtc, ct) ?? [])
            .Where(f => f.ShiftDeletedUtc is null && shiftCompany.ContainsKey(f.ShiftId))
            .Select(f => new DashboardWorkInput(shiftCompany[f.ShiftId], f.ShiftOccurrenceUtc, f.StartedAtUtc, f.EndedAtUtc, f.IsSubmitted)).ToList();

        var names = new Dictionary<Guid, string>();
        foreach (var companyId in shiftCompany.Values.Distinct())
            names[companyId] = (await companies.GetAsync(companyId, ct))?.Name ?? "Customer";
        return DashboardMonthlyRules.Build(visits, work, zone, names);
    }
}
