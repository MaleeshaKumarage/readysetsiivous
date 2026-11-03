using CleaningSuite.Api.Auth;
using CleaningSuite.Application.WorkQueue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Typed work queue over public bookings and partner requests. Admin only until manager access is verified.</summary>
[ApiController]
[Route("api/v1/admin/work-queue")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class WorkQueueAdminController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string view = WorkQueueViews.NeedsAction,
        [FromQuery] string source = "all",
        [FromQuery] string? q = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken ct = default)
    {
        if (!WorkQueueViews.IsKnown(view)) return BadRequest("View must be needs-action, scheduled, in-progress or completed.");
        if (source is not ("all" or "booking" or "partner")) return BadRequest("Source must be all, booking or partner.");
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);
        return Ok(await mediator.Send(new WorkQueueQuery(view, source, q, skip, take), ct));
    }
}
