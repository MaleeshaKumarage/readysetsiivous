using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Bookings.Commands;
using CleaningSuite.Application.Bookings.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Booking management for the tenant admin.</summary>
[ApiController]
[Route("api/v1/admin/bookings")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class BookingsAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public BookingsAdminController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? status,
        [FromQuery] Guid? employeeId,
        CancellationToken ct)
    {
        var bookings = await _mediator.Send(new ListBookingsQuery(fromUtc, toUtc, status, employeeId), ct);
        return Ok(bookings);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var booking = await _mediator.Send(new GetBookingDetailQuery(id), ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] BookingNote? body, CancellationToken ct)
    {
        await _mediator.Send(new ConfirmBookingCommand(id, body?.Note), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] BookingNote? body, CancellationToken ct)
    {
        await _mediator.Send(new CancelBookingCommand(id, body?.Note), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] BookingNote? body, CancellationToken ct)
    {
        await _mediator.Send(new CompleteBookingCommand(id, body?.Note), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/assign")]
    public async Task<IActionResult> Assign(
        Guid id,
        [FromBody] AssignBody body,
        CancellationToken ct)
    {
        await _mediator.Send(
            new CleaningSuite.Application.Employees.Commands.AssignBookingCommand(id, body.EmployeeId), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unassign")]
    public async Task<IActionResult> Unassign(Guid id, CancellationToken ct)
    {
        await _mediator.Send(
            new CleaningSuite.Application.Employees.Commands.UnassignBookingCommand(id), ct);
        return NoContent();
    }
}

public record AssignBody(Guid EmployeeId);

public record BookingNote(string? Note);
