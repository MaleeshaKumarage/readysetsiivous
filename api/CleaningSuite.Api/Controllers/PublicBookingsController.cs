using CleaningSuite.Application.Bookings.Commands;
using CleaningSuite.Application.Bookings.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Anonymous booking endpoints for a tenant's public site. Tenant = URL slug.</summary>
[ApiController]
[Route("api/v1/public/{slug}")]
[AllowAnonymous]
public class PublicBookingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicBookingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("availability")]
    public async Task<IActionResult> Availability(
        [FromQuery] string date, [FromQuery] Guid serviceId, CancellationToken ct)
    {
        var slots = await _mediator.Send(new GetAvailabilityQuery(date, serviceId), ct);
        return Ok(slots);
    }

    [HttpPost("bookings")]
    public async Task<IActionResult> Book(CreatePublicBookingCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("bookings/{bookingNumber}")]
    public async Task<IActionResult> Lookup(
        string bookingNumber, [FromQuery] string phone, CancellationToken ct)
    {
        var booking = await _mediator.Send(new GetPublicBookingQuery(bookingNumber, phone), ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    [HttpPost("bookings/{bookingNumber}/cancel")]
    public async Task<IActionResult> Cancel(
        string bookingNumber, [FromQuery] string phone, CancellationToken ct)
    {
        await _mediator.Send(new CancelPublicBookingCommand(bookingNumber, phone), ct);
        return NoContent();
    }
}
