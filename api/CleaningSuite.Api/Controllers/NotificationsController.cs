using System.Security.Claims;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/me/notifications")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(8192)]
public sealed class NotificationsController(IPushSubscriptions subscriptions) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => Run(a => subscriptions.GetAsync(a, ct));
    [HttpPut("subscription")]
    public Task<IActionResult> Enroll([FromBody] PushSubscriptionInput input, CancellationToken ct) => Run(a => subscriptions.EnrollAsync(a, input, ct));
    [HttpDelete("subscription/{id:guid}")]
    public Task<IActionResult> Disable(Guid id, CancellationToken ct) => Run(async a => { await subscriptions.DisableAsync(a, id, ct); return new { disabled = true }; });
    [HttpPost("subscription/{id:guid}/test")]
    public Task<IActionResult> Test(Guid id, CancellationToken ct) => Run(a => subscriptions.TestAsync(a, id, ct));

    private async Task<IActionResult> Run<T>(Func<PushActor, Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        var employee = HttpContext.Items["Employee"] as Employee;
        var subject = User.FindFirstValue("sub");
        if (employee is null) return Conflict(new { code = "employee_required", detail = "Link this login to an employee profile before enabling notifications." });
        if (!employee.IsActive || employee.IdentitySyncPending || !Employee.IsWorkRole(employee.Role)
            || string.IsNullOrWhiteSpace(subject) || employee.KeycloakUserId != subject) return Forbid();
        try { return Ok(await action(new PushActor(employee.Id, subject))); }
        catch (PushSubscriptionException error)
        {
            var status = error.Code switch {
                "device_not_found" => 404, "rate_limited" => 429, "unavailable" => 503,
                "ownership_conflict" or "device_limit" => 409, _ => 400 };
            return StatusCode(status, new { code = error.Code });
        }
    }
}
