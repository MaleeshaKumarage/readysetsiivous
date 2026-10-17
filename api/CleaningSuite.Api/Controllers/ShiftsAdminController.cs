using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.QualityCycle;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

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
    [OutputCache(Duration = 30)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? branchId,
        CancellationToken ct, [FromQuery] bool includeInactive = false)
    {
        var result = await _mediator.Send(new ListShiftsQuery(companyId, branchId, includeInactive), ct);
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

    [HttpPut("{id:guid}/quality-cycle-template")]
    public async Task<IActionResult> SetTemplate(Guid id, [FromBody] SetTemplateBody body, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetShiftQualityCycleTemplateCommand(id, body.TemplateId), ct));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateShiftCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new ReactivateShiftCommand(id), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { await _mediator.Send(new DeleteShiftCommand(id), ct); return NoContent(); }

    [HttpGet("{id:guid}/assignments")]
    public async Task<IActionResult> GetAssignments(Guid id, CancellationToken ct)
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

    [HttpPut("{id:guid}/visit-assignees")]
    public async Task<IActionResult> SetVisitAssignees(Guid id, [FromBody] SetVisitAssigneesCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command with { ShiftId = id }, ct));

    [HttpPut("{id:guid}/occurrence-exceptions")]
    public async Task<IActionResult> SaveOccurrenceException(Guid id,
        [FromBody] SaveShiftOccurrenceExceptionCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command with { ShiftId = id }, ct));

    [HttpDelete("{id:guid}/occurrence-exceptions/{exceptionId:guid}")]
    public async Task<IActionResult> DeleteOccurrenceException(Guid id, Guid exceptionId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteShiftOccurrenceExceptionCommand(id, exceptionId), ct);
        return NoContent();
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

public record SetTemplateBody(Guid? TemplateId);
