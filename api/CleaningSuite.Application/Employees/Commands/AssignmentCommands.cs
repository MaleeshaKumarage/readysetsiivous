using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Bookings;
using MediatR;

namespace CleaningSuite.Application.Employees.Commands;

public class AssignBookingHandler : IRequestHandler<AssignBookingCommand, Unit>
{
    private readonly IBookingRepository _bookings;
    private readonly IEmployeeRepository _employees;
    private readonly IShiftWorkReservation? _reservation;

    public AssignBookingHandler(IBookingRepository bookings, IEmployeeRepository employees, IShiftWorkReservation? reservation = null)
    {
        _bookings = bookings;
        _employees = employees;
        _reservation = reservation;
    }

    public async Task<Unit> Handle(AssignBookingCommand request, CancellationToken ct)
    {
        await using var reservation = _reservation is null ? null : await _reservation.AcquireAsync(ct);
        var booking = await _bookings.GetByIdAsync(request.BookingId, ct)
            ?? throw new NotFoundException("Booking", request.BookingId);

        var employee = await _employees.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", request.EmployeeId);

        if (!employee.IsActive || !CleaningSuite.Domain.Employees.Employee.IsWorkRole(employee.Role))
            throw new SlotConflictException("Employee is not active");

        // Conflict check against this employee's other bookings in the same window.
        var overlaps = await _bookings.FindOverlappingAsync(
            booking.StartUtc, booking.EndUtc, booking.Id, ct);
        var employeeConflict = overlaps.Any(b => b.EmployeeId == request.EmployeeId.ToString());
        if (employeeConflict)
            throw new SlotConflictException("Employee already booked in this window");

        booking.EmployeeId = request.EmployeeId.ToString();
        booking.EmployeeName = $"{employee.FirstName} {employee.LastName}";
        booking.UpdatedUtc = DateTime.UtcNow;
        booking.History.Add(new StatusEvent
        {
            Status = booking.Status,
            By = "admin",
            Note = $"Assigned to {booking.EmployeeName}",
        });

        await _bookings.SaveAsync(booking, ct);
        return Unit.Value;
    }
}

public class UnassignBookingHandler : IRequestHandler<UnassignBookingCommand, Unit>
{
    private readonly IBookingRepository _bookings;

    public UnassignBookingHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<Unit> Handle(UnassignBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, ct)
            ?? throw new NotFoundException("Booking", request.BookingId);

        booking.EmployeeId = null;
        booking.EmployeeName = null;
        booking.UpdatedUtc = DateTime.UtcNow;
        booking.History.Add(new StatusEvent { Status = booking.Status, By = "admin", Note = "Unassigned" });

        await _bookings.SaveAsync(booking, ct);
        return Unit.Value;
    }
}
