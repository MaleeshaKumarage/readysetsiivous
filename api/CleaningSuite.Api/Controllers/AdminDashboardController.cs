using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/dashboard")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class AdminDashboardController(IMediator mediator) : ControllerBase
{
    /// <summary>Visits and hours per month and customer, for the dashboard charts.</summary>
    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly([FromQuery] int months = 6, CancellationToken ct = default) =>
        Ok(await mediator.Send(new GetAdminDashboardMonthlyQuery(months), ct));
}
