using CleaningSuite.Application.Tenants.Commands;
using CleaningSuite.Application.Tenants.Queries;
using CleaningSuite.Api.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Tenant profile + dynamic page content for the authenticated admin.</summary>
[ApiController]
[Route("api/v1/admin/tenant")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class TenantAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public TenantAdminController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var profile = await _mediator.Send(new GetTenantProfileQuery(), ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateTenantProfileCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }

    [HttpPut("pages/{pageKey}")]
    public async Task<IActionResult> UpdatePage(
        string pageKey, Dictionary<string, string> values, CancellationToken ct)
    {
        await _mediator.Send(new UpdateTenantPageCommand(pageKey, values), ct);
        return NoContent();
    }
}
