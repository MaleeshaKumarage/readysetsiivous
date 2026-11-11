using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Shifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Visits on a branch's main shift, for administrators and managers. No response carries a price.</summary>
[ApiController]
[Route("api/v1/admin/branch-visits")]
[Route("api/v1/manager/branch-visits")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BranchVisitsController(BranchVisitService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid branchId, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
    {
        if (to <= from || to - from > TimeSpan.FromDays(400))
            throw new FluentValidation.ValidationException("Choose a visit window of at most 400 days.");
        var main = await service.Get(branchId, DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc), ct);
        return main is null ? NoContent() : Ok(main);
    }

    [HttpPost]
    public async Task<IActionResult> Add(AddBranchVisit body, CancellationToken ct) => Ok(await service.Add(body, ct));

    [HttpPut("shifts/{shiftId:guid}/default-employee")]
    public async Task<IActionResult> SetDefaultEmployee(Guid shiftId, SetDefaultEmployee body, CancellationToken ct) =>
        Ok(await service.SetDefaultEmployee(shiftId, body, ct));

    [HttpPut("shifts/{shiftId:guid}/visits/{visitId:guid}/employee")]
    public async Task<IActionResult> SetEmployee(Guid shiftId, Guid visitId, SetVisitEmployee body, CancellationToken ct) =>
        Ok(await service.SetEmployee(shiftId, visitId, body, ct));
}
