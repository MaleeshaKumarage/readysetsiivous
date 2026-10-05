using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Employees;
using MediatR;

namespace CleaningSuite.Application.Employees.Queries;

public record ListEmployeesQuery(bool IncludeInactive) : IRequest<IReadOnlyList<Employee>>;

public class ListEmployeesHandler : IRequestHandler<ListEmployeesQuery, IReadOnlyList<Employee>>
{
    private readonly IEmployeeRepository _employees;
    private readonly ITenantCacheService? _cacheService;

    public ListEmployeesHandler(IEmployeeRepository employees, ITenantCacheService? cacheService = null)
    {
        _employees = employees;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<Employee>> Handle(ListEmployeesQuery request, CancellationToken ct)
    {
        var fetch = (CancellationToken cToken) => _employees.ListAsync(request.IncludeInactive, cToken);
        if (_cacheService is null) return await fetch(ct);
        return (await _cacheService.GetOrAddAsync("employees", $"list_{request.IncludeInactive}", fetch, ct: ct))!;
    }
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
