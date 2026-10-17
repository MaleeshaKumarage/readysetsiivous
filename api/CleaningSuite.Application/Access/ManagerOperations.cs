using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Access;

public record ManagerEmployeeResponse(Guid Id, string FirstName, string LastName, string Phone,
    bool IsActive, string? ColorHex, IReadOnlyList<string> Skills, IReadOnlyList<string> ServiceAreas,
    IReadOnlyDictionary<string, HoursResponse> DefaultHours);
public record ManagerCompanyResponse(Guid Id, string Name, bool IsActive);
public record ManagerBranchResponse(Guid Id, Guid CompanyId, string Name, bool IsActive,
    string? Street, string? PostalCode, string? City, string? Country, string? ContactPhone);
public record ScheduleRangeResponse(TimeSpan Start, TimeSpan End);
public record ManagerScheduleResponse(ShiftScheduleType Type, TimeSpan? DailyStart, TimeSpan? DailyEnd,
    IReadOnlyDictionary<DayOfWeek, ScheduleRangeResponse> DailyTimes, DayOfWeek? WeeklyDay,
    IReadOnlyList<DayOfWeek> WeeklyDays, TimeSpan? WeeklyStart, TimeSpan? WeeklyEnd,
    int? BiWeeklyWeekParity, DayOfWeek? BiWeeklyDay, TimeSpan? BiWeeklyStart, TimeSpan? BiWeeklyEnd,
    int? MonthlyDay, TimeSpan? MonthlyStart, TimeSpan? MonthlyEnd);
public record ManagerShiftResponse(Guid Id, Guid CompanyId, Guid BranchId, string Name,
    ManagerScheduleResponse Schedule, bool IsActive, DateTime? ValidFrom, DateTime? ValidUntil,
    string TimeZoneId = "Europe/Helsinki");
public record ManagerAssignmentResponse(Guid Id, Guid ShiftId, Guid EmployeeId, DateTime AssignedAtUtc, bool IsActive);

public static class ManagerResponses
{
    public static ManagerEmployeeResponse Employee(Employee e) => new(e.Id, e.FirstName, e.LastName,
        e.Phone, e.IsActive, e.ColorHex, e.Skills.ToArray(), e.ServiceAreas.ToArray(), AdminResponses.Hours(e.DefaultHours));
    public static ManagerScheduleResponse Schedule(ShiftSchedule s) => new(s.Type, s.DailyStart, s.DailyEnd,
        s.DailyTimes.ToDictionary(x => x.Key, x => new ScheduleRangeResponse(x.Value.Start, x.Value.End)),
        s.WeeklyDay, s.WeeklyDays.ToArray(), s.WeeklyStart, s.WeeklyEnd, s.BiWeeklyWeekParity,
        s.BiWeeklyDay, s.BiWeeklyStart, s.BiWeeklyEnd, s.MonthlyDay, s.MonthlyStart, s.MonthlyEnd);
    public static ManagerShiftResponse Shift(Shift s, string timeZoneId = "Europe/Helsinki") => new(s.Id, s.CompanyId, s.BranchId, s.Name,
        Schedule(s.Schedule), s.IsActive, s.ValidFrom, s.ValidUntil, timeZoneId);
    public static ManagerAssignmentResponse Assignment(ShiftAssignmentDto a) => new(a.Id, a.ShiftId, a.EmployeeId, a.AssignedAtUtc, a.IsActive);
}

/// <summary>Tenant-scoped operational contracts; no administrative persistence serialization.</summary>
public sealed class ManagerOperations(IShiftRepository shifts, IEmployeeRepository employees,
    ICompanyRepository companies, IBranchRepository branches, IQualityCycleDispatcher dispatcher,
    IMediator mediator, ITenantProfileRepository? profiles = null, ITenantContext? tenant = null)
{
    public async Task<string> TimeZone(CancellationToken ct)
    { var (id, _) = await TenantTimeZone.LoadAsync(profiles, tenant, ct); return id; }

    public async Task<IReadOnlyList<ManagerEmployeeResponse>> Roster(CancellationToken ct) =>
        (await employees.ListAsync(false, ct)).Where(e => e.IsActive).Select(ManagerResponses.Employee).ToArray();

    public async Task<Paged<ManagerCompanyResponse>> Companies(int skip, int take, CancellationToken ct)
    {
        if (skip < 0 || take is < 1 or > 100) throw new ValidationException("Use a page size between 1 and 100.");
        var page = await companies.ListAsync(null, skip, take, ct);
        return new(page.Items.Where(c => c.IsActive).Select(c => new ManagerCompanyResponse(c.Id, c.Name, c.IsActive)).ToArray(), page.Total);
    }

    public async Task<IReadOnlyList<ManagerBranchResponse>> Branches(Guid companyId, CancellationToken ct)
    {
        var company = await companies.GetAsync(companyId, ct) ?? throw new NotFoundException("Company", companyId);
        if (!company.IsActive) throw new NotFoundException("Company", companyId);
        return (await branches.ListByCompanyAsync(companyId, ct)).Where(b => b.CompanyId == companyId && b.IsActive)
            .Select(b => new ManagerBranchResponse(b.Id, b.CompanyId, b.Name, b.IsActive,
                b.Street, b.PostalCode, b.City, b.Country, b.ContactPhone)).ToArray();
    }

    public async Task<IReadOnlyList<ManagerShiftResponse>> List(CancellationToken ct, bool includeInactive = false)
    {
        var (timeZoneId, _) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var result = new List<ManagerShiftResponse>();
        foreach (var shift in includeInactive ? await shifts.ListIncludingInactiveAsync(ct) : await shifts.ListAsync(null, null, ct)) {
            if (await ValidParents(shift.CompanyId, shift.BranchId, ct)) result.Add(ManagerResponses.Shift(shift, timeZoneId));
        }
        return result;
    }

    public async Task<ManagerShiftResponse> Get(Guid id, CancellationToken ct)
    { var (timeZoneId, _) = await TenantTimeZone.LoadAsync(profiles, tenant, ct); return ManagerResponses.Shift(await Load(id, ct), timeZoneId); }

    public async Task<ManagerShiftResponse> Create(Guid companyId, Guid branchId, string name,
        ShiftSchedule schedule, DateTime? validFrom, DateTime? validUntil, CancellationToken ct)
    {
        ValidateSchedule(schedule, validFrom, validUntil);
        if (!await ValidParents(companyId, branchId, ct)) throw new NotFoundException("Branch", branchId);
        var result = await mediator.Send(new CreateShiftCommand(companyId, branchId, name, schedule,
            ValidFrom: validFrom, ValidUntil: validUntil), ct);
        return await Get(result.Id, ct);
    }

    public async Task<ManagerShiftResponse> Update(Guid id, string name, ShiftSchedule schedule,
        bool active, DateTime? validFrom, DateTime? validUntil, CancellationToken ct)
    {
        var shift = await Load(id, ct);
        ValidateSchedule(schedule, validFrom, validUntil);
        // Validate before mutation. Retain the same versioned document and all admin-owned fields.
        await new ShiftValidators.UpdateShiftCommandValidator().ValidateAndThrowAsync(new(id, shift.CompanyId, shift.BranchId,
            name, schedule, shift.Notes, active, validFrom, validUntil, shift.QualityCycleTemplateId), ct);
        shift.EnsureQualityCycleHistory();
        shift.Update(shift.CompanyId, shift.BranchId, name, schedule, shift.Notes,
            active, validFrom, validUntil, shift.QualityCycleTemplateId);
        if (shift.QualityCyclePlans.Count > 0 || shift.QualityCycleTemplateId.HasValue) shift.RecordQualityCyclePlan(DateTime.UtcNow);
        await shifts.SaveAsync(shift, ct);
        if (active) await dispatcher.EnsureUpcomingAsync(id, ct);
        return ManagerResponses.Shift(shift);
    }

    public async Task Deactivate(Guid id, CancellationToken ct)
    {
        await Load(id, ct); await mediator.Send(new DeactivateShiftCommand(id), ct);
    }

    public async Task Delete(Guid id, CancellationToken ct)
    { await mediator.Send(new DeleteShiftCommand(id), ct); }

    public async Task<ManagerShiftResponse> Reactivate(Guid id, CancellationToken ct)
    {
        await Load(id, ct); await mediator.Send(new ReactivateShiftCommand(id), ct); return await Get(id, ct);
    }

    public async Task<IReadOnlyList<ManagerAssignmentResponse>> Assignments(Guid id, CancellationToken ct)
    {
        await Load(id, ct);
        return (await shifts.ListAssignmentsByShiftAsync(id, ct)).Where(a => a.ShiftId == id && a.IsActive)
            .Select(a => new ManagerAssignmentResponse(a.Id, a.ShiftId, a.EmployeeId, a.AssignedAtUtc, a.IsActive)).ToArray();
    }

    public async Task<ManagerAssignmentResponse> Assign(Guid id, Guid employeeId, CancellationToken ct)
    {
        await Load(id, ct);
        var existing = await shifts.GetAssignmentAsync(id, employeeId, ct);
        return ManagerResponses.Assignment(await mediator.Send(new AssignEmployeeToShiftCommand(id, employeeId, existing?.Note), ct));
    }

    public async Task Unassign(Guid id, Guid employeeId, CancellationToken ct)
    {
        await Load(id, ct); await mediator.Send(new RemoveEmployeeFromShiftCommand(id, employeeId), ct);
    }

    public async Task<IReadOnlyList<ShiftOccurrenceDto>> Occurrences(Guid id, DateTime from, DateTime to, CancellationToken ct)
    { await Load(id, ct); return await mediator.Send(new GetShiftOccurrencesQuery(id, from, to), ct); }

    public async Task<ShiftOccurrenceDto> SaveOccurrence(Guid id, SaveShiftOccurrenceExceptionCommand command, CancellationToken ct)
    { await Load(id, ct); return await mediator.Send(command with { ShiftId = id }, ct); }

    public async Task DeleteOccurrence(Guid id, Guid exceptionId, CancellationToken ct)
    { await Load(id, ct); await mediator.Send(new DeleteShiftOccurrenceExceptionCommand(id, exceptionId), ct); }

    private async Task<Shift> Load(Guid id, CancellationToken ct)
    {
        var shift = await shifts.GetAsync(id, ct);
        if (shift is null || shift.Id != id || !await ValidParents(shift.CompanyId, shift.BranchId, ct))
            throw new NotFoundException("Shift", id);
        return shift;
    }

    private async Task<bool> ValidParents(Guid companyId, Guid branchId, CancellationToken ct)
    {
        var company = await companies.GetAsync(companyId, ct); var branch = await branches.GetAsync(branchId, ct);
        return company is { IsActive: true } && company.Id == companyId && branch is { IsActive: true }
            && branch.Id == branchId && branch.CompanyId == companyId;
    }

    private static void ValidateSchedule(ShiftSchedule schedule, DateTime? from, DateTime? until)
    {
        if (schedule is null) throw new ValidationException("Schedule is required.");
        if (schedule.DailyTimes is null || schedule.WeeklyDays is null) throw new ValidationException("Schedule collections cannot be null.");
        try { schedule.Validate(); } catch (ArgumentException e) { throw new ValidationException(e.Message); }
        var times = new[] { schedule.DailyStart, schedule.DailyEnd, schedule.WeeklyStart, schedule.WeeklyEnd,
            schedule.BiWeeklyStart, schedule.BiWeeklyEnd, schedule.MonthlyStart, schedule.MonthlyEnd }
            .Concat(schedule.DailyTimes.Values.SelectMany(r => new TimeSpan?[] { r.Start, r.End }));
        if (times.Any(t => t.HasValue && (t < TimeSpan.Zero || t > TimeSpan.FromDays(1))))
            throw new ValidationException("Shift times must be within one calendar day.");
        var days = schedule.WeeklyDays.Concat(schedule.DailyTimes.Keys)
            .Concat(new[] { schedule.WeeklyDay, schedule.BiWeeklyDay }.Where(d => d.HasValue).Select(d => d!.Value));
        if (days.Any(d => !Enum.IsDefined(d))) throw new ValidationException("Choose a valid weekday.");
        if (new[] { from, until }.Any(d => d.HasValue && d.Value.Year is < 2 or > 9998)
            || (from.HasValue && until.HasValue && until < from))
            throw new ValidationException("Choose supported validity dates with ValidUntil on or after ValidFrom.");
    }
}
