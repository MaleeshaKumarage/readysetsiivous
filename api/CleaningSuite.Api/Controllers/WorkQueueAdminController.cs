using CleaningSuite.Api.Auth;
using CleaningSuite.Application.WorkQueue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Typed work queue over public bookings, partner requests and company shifts. Admin only until manager access is verified.</summary>
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
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? assignee = null,
        [FromQuery] bool unassigned = false,
        CancellationToken ct = default)
    {
        if (!WorkQueueViews.IsKnown(view)) return BadRequest("View must be needs-action, scheduled, in-progress, completed or cancelled.");
        if (source is not ("all" or "booking" or "partner" or "shift")) return BadRequest("Source must be all, booking, partner or shift.");
        if (from is not null && !DateOnly.TryParseExact(from, "yyyy-MM-dd", out _)) return BadRequest("From must be a yyyy-MM-dd date.");
        if (to is not null && !DateOnly.TryParseExact(to, "yyyy-MM-dd", out _)) return BadRequest("To must be a yyyy-MM-dd date.");
        if (from is not null && to is not null && string.CompareOrdinal(from, to) > 0) return BadRequest("From must not be after To.");
        if (assignee is not null && string.IsNullOrWhiteSpace(assignee)) return BadRequest("Assignee must not be blank.");
        if (unassigned && assignee is not null) return BadRequest("Choose either an assignee or unassigned work, not both.");
        // Each source reads skip+take rows before merging, so bound skip to keep that window small and non-negative.
        skip = Math.Clamp(skip, 0, WorkQueueLimits.MaxSkip);
        take = Math.Clamp(take, 1, 100);
        return Ok(await mediator.Send(new WorkQueueQuery(view, source, q, skip, take, from, to, assignee?.Trim(), unassigned), ct));
    }
}
