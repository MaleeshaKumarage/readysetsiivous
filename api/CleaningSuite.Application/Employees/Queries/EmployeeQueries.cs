using CleaningSuite.Application.Bookings;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Employees;
using MediatR;

namespace CleaningSuite.Application.Employees.Queries;

public record ListEmployeesQuery(bool IncludeInactive) : IRequest<IReadOnlyList<Employee>>;

public class ListEmployeesHandler : IRequestHandler<ListEmployeesQuery, IReadOnlyList<Employee>>
{
    private readonly IEmployeeRepository _employees;

    public ListEmployeesHandler(IEmployeeRepository employees) => _employees = employees;

    public Task<IReadOnlyList<Employee>> Handle(ListEmployeesQuery request, CancellationToken ct) =>
        _employees.ListAsync(request.IncludeInactive, ct);
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
