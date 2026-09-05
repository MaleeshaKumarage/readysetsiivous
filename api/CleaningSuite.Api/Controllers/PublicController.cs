using CleaningSuite.Application.Services.Queries;
using CleaningSuite.Application.Tenants.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Anonymous endpoints for a tenant's public site. Tenant = URL slug.</summary>
[ApiController]
[Route("api/v1/public/{slug}")]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicController(IMediator mediator) => _mediator = mediator;

    [HttpGet("services")]
    public async Task<IActionResult> Services([FromQuery] string lang = "fi", CancellationToken ct = default)
    {
        var services = await _mediator.Send(new GetPublicServicesQuery(lang), ct);
        return Ok(services);
    }

    [HttpGet("content")]
    public async Task<IActionResult> Content([FromQuery] string lang = "fi", CancellationToken ct = default)
    {
        var content = await _mediator.Send(new GetPublicContentQuery(lang), ct);
        return content is null ? NotFound() : Ok(content);
    }
}
