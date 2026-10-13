using CleaningSuite.Application.Tenants.Commands;
using CleaningSuite.Api.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Cross-tenant lifecycle operations require explicit platform permission.</summary>
[ApiController]
[Route("api/v1/system/tenants")]
[Authorize(Policy = PlatformAuthorization.PolicyName)]
public class SystemController : ControllerBase
{
    private readonly IMediator _mediator;

    public SystemController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Provision(ProvisionTenantCommand command, CancellationToken ct)
    {
        HttpContext.Items["PlatformTargetTenant"] = command.Slug;
        var result = await _mediator.Send(command, ct);
        return result.AlreadyExisted
            ? Conflict(new { slug = result.Slug, title = "Tenant already registered" })
            : StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenants = await _mediator.Send(new ListTenantsQuery(), ct);
        return Ok(tenants);
    }

    [HttpPost("{slug}/suspend")]
    public async Task<IActionResult> Suspend(string slug, CancellationToken ct)
    {
        await _mediator.Send(new SetTenantStatusCommand(slug, Active: false), ct);
        return NoContent();
    }

    [HttpPost("{slug}/activate")]
    public async Task<IActionResult> Activate(string slug, CancellationToken ct)
    {
        await _mediator.Send(new SetTenantStatusCommand(slug, Active: true), ct);
        return NoContent();
    }

}
