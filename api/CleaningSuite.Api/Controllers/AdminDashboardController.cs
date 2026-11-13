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

    /// <summary>Visits and hours for each day of one month (month=yyyy-MM), for the daily charts.</summary>
    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] string? month, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(month) || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out _))
            return Problem(title: "Invalid month", detail: "Use month=yyyy-MM.", statusCode: StatusCodes.Status400BadRequest);
        return Ok(await mediator.Send(new GetAdminDashboardDailyQuery(month), ct));
    }

    /// <summary>Scheduled hours per employee for one week (Monday to Sunday). Defaults to the current week.</summary>
    [HttpGet("employee-hours")]
    public async Task<IActionResult> EmployeeHours([FromQuery] string? weekStart, CancellationToken ct = default)
    {
        DateOnly? week = null;
        if (!string.IsNullOrWhiteSpace(weekStart))
        {
            if (!DateOnly.TryParseExact(weekStart, "yyyy-MM-dd", out var parsed))
                return Problem(title: "Invalid week", detail: "Use weekStart=yyyy-MM-dd.", statusCode: StatusCodes.Status400BadRequest);
            week = parsed;
        }
        return Ok(await mediator.Send(new GetAdminDashboardEmployeeHoursQuery(week), ct));
    }
}
