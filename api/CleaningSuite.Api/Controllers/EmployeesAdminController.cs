using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Employees.PayRates;
using CleaningSuite.Application.Employees.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace CleaningSuite.Api.Controllers;

/// <summary>Employee management for the tenant admin.</summary>
[ApiController]
[Route("api/v1/admin/employees")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class EmployeesAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesAdminController(IMediator mediator) => _mediator = mediator;

    [HttpPut("{id:guid}/shift-calendar-access")]
    public async Task<IActionResult> CalendarAccess(Guid id, CalendarAccessBody body,
        [FromServices] CleaningSuite.Application.Employees.IEmployeeRepository employees, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(id, ct);
        if (employee is null) return NotFound();
        if (employee.Role != CleaningSuite.Domain.Employees.Employee.RoleAdmin || !employee.IsActive || employee.IdentitySyncPending)
            return BadRequest(new { detail = "Calendar access can only be changed for active, synchronized admins." });
        employee.ShiftCalendarEnabled = body.Enabled;
        employee.UpdatedUtc = DateTime.UtcNow;
        employee.ShiftCalendarAccessChanges.Add(new(User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException(), employee.UpdatedUtc, body.Enabled));
        await employees.SaveAsync(employee, ct);
        return Ok(new { enabled = employee.CanUseShiftCalendar });
    }
    public record CalendarAccessBody(bool Enabled);

    [HttpGet]
    [OutputCache(NoStore = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var employees = await _mediator.Send(new ListEmployeesQuery(includeInactive), ct);
        return Ok(employees.Select(AdminResponses.Employee).ToArray());
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateEmployeeCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateEmployeeCommand command, CancellationToken ct)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateEmployeeCommand(id), ct);
        return NoContent();
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ReactivateEmployeeCommand(id), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { await _mediator.Send(new DeleteEmployeeCommand(id), ct); return NoContent(); }

    /// <summary>Admin-only pay profile: dated hourly rates and the legacy rate's confirmation state.</summary>
    [HttpGet("{id:guid}/pay-profile")]
    [OutputCache(NoStore = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PayProfile(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetEmployeePayProfileQuery(id), ct));

    [HttpPost("{id:guid}/pay-rates")]
    public async Task<IActionResult> AddPayRate(Guid id, AddPayRateBody body, CancellationToken ct) =>
        Ok(await _mediator.Send(new AddEmployeePayRateCommand(id, body.HourlyRate, body.EffectiveFrom, AdminSubject()), ct));
    public record AddPayRateBody(decimal HourlyRate, DateOnly EffectiveFrom);

    [HttpPost("{id:guid}/pay-profile/legacy-rate")]
    public async Task<IActionResult> ConfirmLegacyPayRate(Guid id, ConfirmLegacyPayRateBody body, CancellationToken ct) =>
        Ok(await _mediator.Send(new ConfirmLegacyPayRateCommand(id, body.EffectiveFrom, AdminSubject()), ct));
    public record ConfirmLegacyPayRateBody(DateOnly EffectiveFrom);

    private string AdminSubject() => User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();

    /// <summary>Links the employee login and sends a secure Keycloak account-setup email; never returns passwords.</summary>
    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/invite")]
    public async Task<IActionResult> Invite(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new InviteEmployeeCommand(id), ct);
        return Ok(result);
    }
}
