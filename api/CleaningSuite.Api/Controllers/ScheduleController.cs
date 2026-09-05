using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Employees.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Day view of one employee's bookings.</summary>
[ApiController]
[Route("api/v1/admin/schedule")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class ScheduleController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScheduleController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string date, [FromQuery] Guid employeeId, CancellationToken ct)
    {
        var bookings = await _mediator.Send(new GetScheduleQuery(date, employeeId), ct);
        return Ok(bookings);
    }
}
