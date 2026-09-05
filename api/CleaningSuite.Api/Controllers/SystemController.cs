using CleaningSuite.Application.Tenants.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Cross-tenant onboarding. Provisioner token (master realm) only.</summary>
[ApiController]
[Route("api/v1/system/tenants")]
[Authorize]
public class SystemController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly string _provisionerRealm;

    public SystemController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _provisionerRealm = configuration["Auth:Keycloak:ProvisionerRealm"] ?? "master";
    }

    [HttpPost]
    public async Task<IActionResult> Provision(ProvisionTenantCommand command, CancellationToken ct)
    {
        if (!IsProvisioner())
            return Forbid();

        var result = await _mediator.Send(command, ct);
        return result.AlreadyExisted
            ? Conflict(new { slug = result.Slug, title = "Tenant already registered" })
            : StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!IsProvisioner())
            return Forbid();

        var tenants = await _mediator.Send(new ListTenantsQuery(), ct);
        return Ok(tenants);
    }

    [HttpPost("{slug}/suspend")]
    public async Task<IActionResult> Suspend(string slug, CancellationToken ct)
    {
        if (!IsProvisioner())
            return Forbid();

        await _mediator.Send(new SetTenantStatusCommand(slug, Active: false), ct);
        return NoContent();
    }

    [HttpPost("{slug}/activate")]
    public async Task<IActionResult> Activate(string slug, CancellationToken ct)
    {
        if (!IsProvisioner())
            return Forbid();

        await _mediator.Send(new SetTenantStatusCommand(slug, Active: true), ct);
        return NoContent();
    }

    private bool IsProvisioner() =>
        (HttpContext.Items["Realm"] as string ?? "") == _provisionerRealm;
}
