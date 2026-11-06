using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace CleaningSuite.Application.Shifts;

public sealed record ShiftOverlapVisit(Guid ShiftId, string ShiftName, DateTime StartUtc, DateTime EndUtc);

public sealed record ShiftOverlapFinding(Guid EmployeeId, ShiftOverlapVisit Earlier, ShiftOverlapVisit Later);

// Finds cleaners booked on two visits that overlap in time. Back-to-back visits (one ends when the next starts) are not overlaps.
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

public sealed record EmployeeDaySchedule(Guid EmployeeId, DateTime LocalDay, IReadOnlyList<ShiftOverlapVisit> Visits);

public record EmployeeScheduleQuery(DateTime From, DateTime To) : IRequest<IReadOnlyList<EmployeeDaySchedule>>;

// Shared by the overlap audit and the employee-by-day schedule so both see the same visits.
public static class ScheduleVisits
{
    public static async Task<List<(Guid EmployeeId, ShiftOverlapVisit Visit)>> GatherAsync(
        IShiftRepository repository, TimeZoneInfo zone, DateTime localFrom, DateTime localTo, CancellationToken ct)
    {
        var fromUtc = TenantTimeZone.StartOfLocalDay(localFrom, zone);
        var toUtc = TenantTimeZone.StartOfLocalDay(localTo.AddDays(1), zone);
        var visits = new List<(Guid EmployeeId, ShiftOverlapVisit Visit)>();
        foreach (var shift in (await repository.ListForReportingAsync(ct)).Where(s => s.IsActive))
        {
            var roster = await repository.ResolveRosterAsync(shift, fromUtc, toUtc, zone, ct);
            foreach (var entry in roster)
                foreach (var employee in entry.EmployeeIds)
                    visits.Add((employee, new ShiftOverlapVisit(shift.Id, shift.Name, entry.Occurrence.StartUtc, entry.Occurrence.EndUtc)));
        }
        return visits;
    }

    // Groups visits by cleaner and tenant-local day, earliest visit first.
    public static IReadOnlyList<EmployeeDaySchedule> GroupByDay(IEnumerable<(Guid EmployeeId, ShiftOverlapVisit Visit)> visits, TimeZoneInfo zone) =>
        visits.GroupBy(x => (x.EmployeeId, Day: TenantTimeZone.LocalDate(x.Visit.StartUtc, zone)))
            .Select(g => new EmployeeDaySchedule(g.Key.EmployeeId, g.Key.Day, g.Select(x => x.Visit).OrderBy(v => v.StartUtc).ToList()))
            .OrderBy(x => x.LocalDay).ThenBy(x => x.EmployeeId)
            .ToList();
}

public sealed class EmployeeScheduleQueryHandler(IShiftRepository repository, ITenantProfileRepository profiles, ITenantContext tenant)
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
        return ScheduleVisits.GroupByDay(await ScheduleVisits.GatherAsync(repository, zone, localFrom, localTo, ct), zone);
    }
}
