using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Services.Commands;
using CleaningSuite.Application.Services.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Service catalog management for the tenant admin.</summary>
[ApiController]
[Route("api/v1/admin/services")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class ServicesAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public ServicesAdminController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var services = await _mediator.Send(new ListServicesQuery(includeInactive), ct);
        return Ok(services);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateServiceCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateServiceCommand command, CancellationToken ct)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new SoftDeleteServiceCommand(id), ct);
        return NoContent();
    }
}
