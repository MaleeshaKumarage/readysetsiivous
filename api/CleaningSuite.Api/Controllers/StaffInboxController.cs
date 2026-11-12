using System.Security.Claims;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/me/inbox")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffInboxController(IStaffInbox inbox) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (Me() is not { } id) return Forbid();
        return Ok(await inbox.ListAsync(id, ct));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        if (Me() is not { } employeeId) return Forbid();
        return await inbox.MarkReadAsync(employeeId, id, ct) ? Ok(new { read = true }) : NotFound();
    }

    // Only the signed-in admin or manager sees their own items.
    private Guid? Me()
    {
        var employee = HttpContext.Items["Employee"] as Employee;
        var subject = User.FindFirstValue("sub");
        return employee is { IsActive: true, IdentitySyncPending: false } && (employee.Role is Employee.RoleAdmin or Employee.RoleManager)
            && !string.IsNullOrEmpty(subject) && employee.KeycloakUserId == subject ? employee.Id : null;
    }
}
