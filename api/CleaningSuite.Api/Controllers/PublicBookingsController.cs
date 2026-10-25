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

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpPost("bookings")]
    public async Task<IActionResult> Book(CreatePublicBookingCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpPost("bookings/lookup")]
    public async Task<IActionResult> Lookup(
        [FromBody] GetPublicBookingQuery body, CancellationToken ct)
    {
        var booking = await _mediator.Send(body, ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpPost("bookings/{bookingNumber}/cancel")]
    public async Task<IActionResult> Cancel(
        string bookingNumber, [FromBody] CancelBookingBody body, CancellationToken ct)
    {
        await _mediator.Send(new CancelPublicBookingCommand(bookingNumber, body.ManagementKey), ct);
        return NoContent();
    }
}

public record CancelBookingBody(string? ManagementKey)
{
    public override string ToString() => nameof(CancelBookingBody);
}
