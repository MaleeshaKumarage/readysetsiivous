using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Employees.Commands;
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

    [HttpGet]
    [OutputCache(Duration = 30)]
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

    /// <summary>Links the employee login and sends a secure Keycloak account-setup email; never returns passwords.</summary>
    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/invite")]
    public async Task<IActionResult> Invite(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new InviteEmployeeCommand(id), ct);
        return Ok(result);
    }
}
