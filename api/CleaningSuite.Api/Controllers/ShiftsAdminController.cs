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
        if (id != command.Id)
            return BadRequest("Shift id mismatch.");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateShiftCommand(id), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/occurrences")]
    public async Task<IActionResult> Occurrences(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        try
        {
            var result = await _mediator.Send(new GetShiftOccurrencesQuery(id, from, to), ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:guid}/assignments")]
    public IActionResult Assignments(Guid id, CancellationToken ct)
    {
        // Not implemented: returning an empty array here would be indistinguishable
        // from a shift that genuinely has no assignees, causing clients (e.g.
        // adminShifts.assignments) to render a misleading "no employees assigned"
        // state. Report 501 until a proper ListShiftAssignmentsQuery exists.
        return StatusCode(
            StatusCodes.Status501NotImplemented,
            "Shift assignments listing is not implemented.");
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> AssignEmployee(
        Guid id,
        [FromBody] AssignEmployeeToShiftCommand command,
        CancellationToken ct)
    {
        if (id != command.ShiftId)
            return BadRequest("Shift id mismatch.");
        var result = await _mediator.Send(command, ct);
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
