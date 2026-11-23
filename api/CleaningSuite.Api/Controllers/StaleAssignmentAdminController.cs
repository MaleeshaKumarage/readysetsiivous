using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Partners;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Read-only dry run of the stale-assignment repair (#312). Nothing is changed by this endpoint.</summary>
[ApiController]
[Route("api/v1/admin/shifts/stale-assignments")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class StaleAssignmentAdminController(IShiftRepository shifts, IPartnerStore partners) : ControllerBase
{
    [HttpGet("dry-run")]
    public async Task<IActionResult> DryRun(CancellationToken ct)
    {
        var items = await StaleAssignmentDryRun.Run(shifts, async ids =>
        {
            var requests = await partners.Session.LoadManyAsync<PartnerCleaningRequest>(ids, ct);
            return requests.ToDictionary(r => r.Id, r => r.Status);
        }, ct);
        return Ok(new { count = items.Count, items });
    }
}
