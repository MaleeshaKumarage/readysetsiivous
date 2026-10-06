using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Shifts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/shifts")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class ShiftsAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public ShiftsAdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? branchId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListShiftsQuery(companyId, branchId), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetShiftQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShiftCommand command, CancellationToken ct)
    {
        command = command with { Id = id };
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateShiftCommand(id), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/assignments")]
    public async Task<IActionResult> ListAssignments(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListShiftAssignmentsQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/occurrences")]
    public async Task<IActionResult> Occurrences(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetShiftOccurrencesQuery(id, from, to), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> AssignEmployee(
        Guid id,
        [FromBody] AssignEmployeeToShiftCommand command,
        CancellationToken ct)
    {
        // The client posts only { employeeId, note }, so ShiftId is not present in
        // the body (defaults to Guid.Empty). Take the shift id from the route instead
        // of rejecting every request on a mismatch.
        var result = await _mediator.Send(command with { ShiftId = id }, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/assignments/{employeeId:guid}")]
    public async Task<IActionResult> RemoveEmployee(
        Guid id,
        Guid employeeId,
        CancellationToken ct)
    {
        await _mediator.Send(new RemoveEmployeeFromShiftCommand(id, employeeId), ct);
        return NoContent();
    }
}
