using System.Globalization;
using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using TimeZoneConverter;

namespace CleaningSuite.Application.Access;

public record EmployeeSelfResponse(Guid Id, string FirstName, string LastName, string Email,
    string Phone, string Role, bool IsActive, string? ColorHex, IReadOnlyList<string> Skills,
    IReadOnlyList<string> ServiceAreas, IReadOnlyDictionary<string, HoursResponse> DefaultHours);
public record OperationalServiceResponse(string NameFi, string NameEn, string NameSv, int DurationMinutes);
public record OperationalCustomerResponse(string Name, string Phone);
public record OperationalBookingResponse(Guid Id, string BookingNumber, string Status, DateTime StartUtc,
    DateTime EndUtc, string StartLocalDate, string StartLocalTime, OperationalServiceResponse Service,
    OperationalCustomerResponse Customer, AddressResponse CleaningAddress);
public record OperationalShiftResponse(Guid Id, string Name, Guid CompanyId, string CompanyName,
    Guid BranchId, string BranchName, AddressResponse CleaningAddress, DateTime StartUtc, DateTime EndUtc);
public record OwnWorkResponse(string Date, string TimeZoneId, IReadOnlyList<OperationalBookingResponse> Bookings,
    IReadOnlyList<OperationalShiftResponse> Shifts);

public static class SelfResponses
{
    public static EmployeeSelfResponse Employee(Employee e) => new(e.Id, e.FirstName, e.LastName,
        e.Email, e.Phone, e.Role, e.IsActive, e.ColorHex, e.Skills.ToArray(), e.ServiceAreas.ToArray(),
        AdminResponses.Hours(e.DefaultHours));
    public static OperationalBookingResponse Booking(Booking b) => new(b.Id, b.BookingNumber, b.Status,
        b.StartUtc, b.EndUtc, b.StartLocalDate, b.StartLocalTime,
        new(b.Service.NameFi, b.Service.NameEn, b.Service.NameSv, b.Service.DurationMinutes),
        new(b.Customer.Name, b.Customer.Phone), AdminResponses.Address(b.CleaningAddress));
}

public record GetOwnWorkQuery(Guid EmployeeId, string Date) : IRequest<OwnWorkResponse>;
public record GetOwnBookingQuery(Guid EmployeeId, Guid BookingId) : IRequest<OperationalBookingResponse?>;

public sealed class GetOwnBookingHandler(IBookingRepository bookings)
    : IRequestHandler<GetOwnBookingQuery, OperationalBookingResponse?>
{
    public async Task<OperationalBookingResponse?> Handle(GetOwnBookingQuery request, CancellationToken ct)
    {
        var booking = await bookings.GetByIdAsync(request.BookingId, ct);
        return booking is not null && booking.Id == request.BookingId
            && Guid.TryParse(booking.EmployeeId, out var owner) && owner == request.EmployeeId
            ? SelfResponses.Booking(booking) : null;
    }
}

public sealed class GetOwnWorkHandler(IBookingRepository bookings, IShiftRepository shifts,
    ICompanyRepository companies, IBranchRepository branches, ITenantProfileRepository profiles,
    ITenantContext tenant) : IRequestHandler<GetOwnWorkQuery, OwnWorkResponse>
{
    public async Task<OwnWorkResponse> Handle(GetOwnWorkQuery request, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) || day == DateOnly.MinValue || day >= DateOnly.MaxValue.AddDays(-1))
            throw Invalid("date", "Use an existing date in YYYY-MM-DD format within supported calendar bounds.");
        var profile = await profiles.GetAsync(tenant.TenantId, ct);
        var zoneId = string.IsNullOrWhiteSpace(profile?.TimeZoneId) ? "Europe/Helsinki" : profile.TimeZoneId;
        TimeZoneInfo zone;
        try { zone = TZConvert.GetTimeZoneInfo(zoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw Invalid("timeZone", "The tenant time zone requires administrator correction."); }
        var from = Boundary(day, zone); var to = Boundary(day.AddDays(1), zone);
        if (to <= from) throw Invalid("date", "This local day does not exist in the tenant time zone.");
        var items = await bookings.ListAsync(from, to, null, request.EmployeeId, ct);
        // The projection checks ownership and actual boundaries even if a repository
        // supplies broad, stale or malformed results. Stored local display text is not authorization.
        var ownBookings = items.Where(b => Guid.TryParse(b.EmployeeId, out var owner) && owner == request.EmployeeId
            && b.StartUtc >= from && b.StartUtc < to).OrderBy(b => b.StartUtc).Select(SelfResponses.Booking).ToArray();
        var ownShifts = new List<OperationalShiftResponse>();
        var assignments = await shifts.ListAssignmentsByEmployeeAsync(request.EmployeeId, ct);
        foreach (var assignment in assignments.Where(a => a.IsActive && a.EmployeeId == request.EmployeeId))
        {
            var shift = await shifts.GetAsync(assignment.ShiftId, ct);
            if (shift is null || shift.Id != assignment.ShiftId || !shift.IsActive) continue;
            var company = await companies.GetAsync(shift.CompanyId, ct);
            var branch = await branches.GetAsync(shift.BranchId, ct);
            if (company is null || company.Id != shift.CompanyId || !company.IsActive
                || branch is null || branch.Id != shift.BranchId || branch.CompanyId != company.Id || !branch.IsActive) continue;
            var (localFrom, localTo) = TenantTimeZone.LocalDatesForUtcRange(from, to, zone);
            foreach (var occurrence in ShiftScheduleCalculator.GenerateOccurrences(shift, localFrom, localTo, zone)
                .Where(o => o.StartUtc >= from && o.StartUtc < to && assignment.Overlaps(o.StartUtc, o.EndUtc)))
                ownShifts.Add(new(shift.Id, shift.Name, company.Id, company.Name, branch.Id, branch.Name,
                    new(branch.Street ?? "", branch.PostalCode ?? "", branch.City ?? "", branch.Country),
                    occurrence.StartUtc, occurrence.EndUtc));
        }
        return new(request.Date, zoneId, ownBookings, ownShifts.OrderBy(s => s.StartUtc).ToArray());
    }

    private static DateTime Boundary(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones change clocks at midnight. Start at the first existing instant;
        // for repeated midnight include the earlier UTC instant, preserving the whole day.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local)) return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
    private static ValidationException Invalid(string field, string message) => new(new[] { new ValidationFailure(field, message) });
}
