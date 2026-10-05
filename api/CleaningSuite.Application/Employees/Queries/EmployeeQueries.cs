using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Employees;
using MediatR;

namespace CleaningSuite.Application.Employees.Queries;

public record EmployeeDto(
    Guid Id,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    string Email,
    string FirstName,
    string LastName,
    string Phone,
    string Role,
    bool IsActive,
    string? ColorHex,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> ServiceAreas,
    decimal? PayRate,
    IReadOnlyList<Certification> Certifications,
    string? Notes,
    IReadOnlyDictionary<string, WorkHours> DefaultHours);

public record ListEmployeesQuery(bool IncludeInactive) : IRequest<IReadOnlyList<EmployeeDto>>;

public class ListEmployeesHandler : IRequestHandler<ListEmployeesQuery, IReadOnlyList<EmployeeDto>>
{
    private readonly IEmployeeRepository _employees;
    private readonly ITenantCacheService? _cacheService;

    public ListEmployeesHandler(IEmployeeRepository employees, ITenantCacheService? cacheService = null)
    {
        _employees = employees;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<EmployeeDto>> Handle(ListEmployeesQuery request, CancellationToken ct)
    {
        var fetch = async (CancellationToken cToken) =>
        {
            var list = await _employees.ListAsync(request.IncludeInactive, cToken);
            return (IReadOnlyList<EmployeeDto>)list.Select(MapDto).ToList();
        };

        if (_cacheService is null) return await fetch(ct);
        return (await _cacheService.GetOrAddAsync("employees", $"list_{request.IncludeInactive}", fetch, ct: ct))!;
    }

    private static EmployeeDto MapDto(Employee e) => new(
        e.Id, e.CreatedUtc, e.UpdatedUtc, e.Email, e.FirstName, e.LastName, e.Phone, e.Role, e.IsActive, e.ColorHex,
        e.Skills, e.ServiceAreas, e.PayRate, e.Certifications, e.Notes, e.DefaultHours);
}

public record GetScheduleQuery(string LocalDate, Guid EmployeeId) : IRequest<IReadOnlyList<Booking>>;

public class GetScheduleHandler : IRequestHandler<GetScheduleQuery, IReadOnlyList<Booking>>
{
    private readonly IBookingRepository _bookings;

    public GetScheduleHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<IReadOnlyList<Booking>> Handle(GetScheduleQuery request, CancellationToken ct)
    {
        var bookings = await _bookings.ListForLocalDateAsync(request.LocalDate, ct);
        return bookings
            .Where(b => b.EmployeeId == request.EmployeeId.ToString())
            .OrderBy(b => b.StartUtc)
            .ToList();
    }
}
