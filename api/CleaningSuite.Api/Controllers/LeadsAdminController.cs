using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Leads;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/leads")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class LeadsAdminController : ControllerBase
{
    private const int MaxTake = 200;
    private readonly IMediator _mediator;

    public LeadsAdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [OutputCache(Duration = 30)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxTake);
        var result = await _mediator.Send(new ListLeadsQuery(search, skip, take), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLeadQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLeadCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeadCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateLeadCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ReactivateLeadCommand(id), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteLeadCommand(id), ct);
        return NoContent();
    }
}
