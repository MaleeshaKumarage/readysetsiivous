using System.Security.Claims;
using CleaningSuite.Api.Controllers;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CleaningSuite.Tests;

public class PushNotificationApiTests
{
    [Fact]
    public async Task Unlinked_owner_cannot_invent_employee_subscription_identity()
    {
        var service = new Mock<IPushSubscriptions>(MockBehavior.Strict);
        var controller = Create(service.Object, null, "owner");
        var result = Assert.IsType<ConflictObjectResult>(await controller.Get(default));
        Assert.Contains("employee_required", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData(Employee.RoleAdmin)]
    [InlineData(Employee.RoleManager)]
    [InlineData(Employee.RoleEmployee)]
    public async Task Verified_work_identity_uses_own_id_without_calendar_grant(string role)
    {
        var employee = new Employee { Role = role, KeycloakUserId = "verified-sub", ShiftCalendarEnabled = false };
        var service = new Mock<IPushSubscriptions>(MockBehavior.Strict);
        service.Setup(x => x.GetAsync(new PushActor(employee.Id, "verified-sub"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushConfiguration(false, null, []));
        var controller = Create(service.Object, employee, "verified-sub");
        Assert.IsType<OkObjectResult>(await controller.Get(default));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        service.VerifyAll();
    }

    [Theory]
    [InlineData(Employee.RolePartner, true, false, "sub")]
    [InlineData(Employee.RoleEmployee, false, false, "sub")]
    [InlineData(Employee.RoleEmployee, true, true, "sub")]
    [InlineData(Employee.RoleEmployee, true, false, "different-sub")]
    public async Task Partner_inactive_pending_or_mismatched_actor_is_denied(string role, bool active, bool pending, string claim)
    {
        var service = new Mock<IPushSubscriptions>(MockBehavior.Strict);
        var employee = new Employee { Role = role, IsActive = active, IdentitySyncPending = pending, KeycloakUserId = "sub" };
        Assert.IsType<ForbidResult>(await Create(service.Object, employee, claim).Get(default));
    }

    [Theory]
    [InlineData("device_not_found", 404)]
    [InlineData("ownership_conflict", 409)]
    [InlineData("rate_limited", 429)]
    public async Task Safe_error_codes_preserve_boundary_status(string code, int expected)
    {
        var employee = new Employee { KeycloakUserId = "sub" };
        var service = new Mock<IPushSubscriptions>();
        service.Setup(x => x.GetAsync(It.IsAny<PushActor>(), It.IsAny<CancellationToken>())).ThrowsAsync(new PushSubscriptionException(code));
        var result = Assert.IsType<ObjectResult>(await Create(service.Object, employee, "sub").Get(default));
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal("{\"code\":\"" + code + "\"}", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    private static NotificationsController Create(IPushSubscriptions service, Employee? employee, string subject)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "test")) };
        if (employee is not null) context.Items["Employee"] = employee;
        return new NotificationsController(service) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
