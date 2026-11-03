using System.Security.Claims;
using System.Text.Json;
using CleaningSuite.Api.Controllers;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class AdminShiftCalendarTests
{
    [Theory]
    [InlineData("initial@example.invalid", "admin", null, false)]
    [InlineData("INITIAL@EXAMPLE.INVALID", "admin", null, false)]
    [InlineData("initial@example.invalid", "admin", false, false)]
    [InlineData("other@hiava.fi", "admin", null, false)]
    [InlineData("other@hiava.fi", "admin", true, true)]
    [InlineData("initial@example.invalid", "employee", true, false)]
    [InlineData("other@hiava.fi", "manager", true, false)]
    public void Permission_is_explicit_opt_in_for_admins_and_can_be_revoked(string email, string role, bool? enabled, bool expected)
    {
        var employee = new Employee { Email = email, Role = role, ShiftCalendarEnabled = enabled };
        Assert.Equal(expected, employee.CanUseShiftCalendar);
        employee.IsActive = false; Assert.False(employee.CanUseShiftCalendar);
        employee.IsActive = true; employee.IdentitySyncPending = true; Assert.False(employee.CanUseShiftCalendar);
    }

    [Theory]
    [InlineData("2026-03-29T03:30")]
    [InlineData("2026-10-25T03:30")]
    [InlineData("invalid")]
    public void Invalid_and_ambiguous_local_times_are_rejected(string local) =>
        Assert.Throws<ArgumentException>(() => ShiftCalendarAdminController.ParseLocal(local, TenantTimeZone.Resolve("Europe/Helsinki")));

    [Fact]
    public void Work_times_are_converted_using_tenant_zone() => Assert.Equal(
        new DateTime(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc),
        ShiftCalendarAdminController.ParseLocal("2026-10-01T08:00", TenantTimeZone.Resolve("Europe/Helsinki")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_time_extra_visit_is_visible_without_a_recurring_schedule(bool partner)
    {
        var f = new Fixture();
        f.Shift.Schedule = new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible };
        f.Shift.ValidUntil = f.Start.Date;
        f.Shift.PartnerRequestId = partner ? Guid.NewGuid() : null;
        var extra = ShiftOccurrenceException.Create(f.Shift.Id, null, f.Start, f.Start.AddHours(2),
            ShiftOccurrenceExceptionStatus.Extra, "One-time cleaning", f.Person.Id);
        f.Shifts.Setup(s => s.ListOccurrenceExceptionsAsync(f.Shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { extra });
        // One-time work can be assigned on the visit itself, without a recurring roster.
        f.Shifts.Setup(s => s.ListAssignmentHistoryAsync(f.Shift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ShiftAssignment>());

        var result = Assert.IsType<OkObjectResult>(await f.Controller.Get(f.Start.ToString("yyyy-MM-dd"), default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var visit = Assert.Single(json.RootElement.GetProperty("occurrences").EnumerateArray());
        Assert.Equal(extra.Id, visit.GetProperty("exceptionId").GetGuid());
        Assert.Equal(f.Start, visit.GetProperty("startUtc").GetDateTime());
        var person = Assert.Single(visit.GetProperty("assignees").EnumerateArray());
        Assert.Equal(f.Person.Id, person.GetProperty("employeeId").GetGuid());
        Assert.Equal(!partner, person.GetProperty("canRecord").GetBoolean());
    }

    private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class Fixture
    {
        public Mock<IEmployeeRepository> Employees = new();
        public Mock<IQualityCycleRepository> Forms = new();
        public Mock<IShiftRepository> Shifts = new();
        public Employee Person = new() { FirstName = "Cleaner", LastName = "One" };
        public Employee Admin = new() { Email = "admin@example.invalid", Role = Employee.RoleAdmin, KeycloakUserId = "admin-subject", ShiftCalendarEnabled = true };
        public Shift Shift;
        public DateTime Start = DateTime.UtcNow.Date.AddDays(-3).AddHours(8);
        public ShiftCalendarAdminController Controller;
        public Fixture()
        {
            Shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily clean", new ShiftSchedule {
                Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, Start.Date, null, null);
            Employees.Setup(e => e.GetByKeycloakUserIdAsync("admin-subject", It.IsAny<CancellationToken>())).ReturnsAsync(Admin);
            Employees.Setup(e => e.GetByIdAsync(Person.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Person);
            Shifts.Setup(s => s.GetAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Shift);
            Shifts.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Shift });
            Employees.Setup(e => e.ListAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Person, Admin });
            Forms.Setup(r => r.ListFormsAsync(null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<QualityCycleForm>());
            Shifts.Setup(s => s.ListAssignmentHistoryAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { ShiftAssignment.Create(Shift.Id, Person.Id, null) });
            var reservation = new Mock<IShiftWorkReservation>();
            reservation.Setup(r => r.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Lease());
            Controller = new(Shifts.Object, Employees.Object, Forms.Object, Mock.Of<ICompanyRepository>(), Mock.Of<IBranchRepository>(),
                null!, null!, reservation.Object);
            Controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim("sub", "admin-subject"), new Claim("email_verified", "true") }, "test")) } };
        }
        public ShiftCalendarAdminController.WorkTimeBody Body(Guid? version = null) => new(Shift.Id, Person.Id, Start, null,
            Start.ToString("yyyy-MM-dd'T'HH:mm"), Start.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm"), "Recorded from timesheet", version);
    }

    [Fact]
    public async Task Denies_disabled_admin_even_with_admin_role()
    {
        var f = new Fixture(); f.Admin.ShiftCalendarEnabled = false;
        Assert.IsType<ForbidResult>(await f.Controller.Get(null, default));
        Assert.IsType<ForbidResult>(await f.Controller.Record(f.Body(), default));
        f.Forms.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Records_missing_work_with_audit_and_scheduled_identity()
    {
        var f = new Fixture(); QualityCycleForm? saved = null;
        f.Forms.Setup(r => r.CreateStartedFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()))
            .Callback<QualityCycleForm, CancellationToken>((form, _) => saved = form).Returns(Task.CompletedTask);
        Assert.IsType<OkObjectResult>(await f.Controller.Record(f.Body(), default));
        Assert.NotNull(saved); Assert.True(saved.IsSubmitted);
        Assert.Equal(f.Start, saved.ShiftOccurrenceUtc); Assert.Equal(f.Start, saved.StartedAtUtc);
        Assert.Equal("admin-subject", Assert.Single(saved.WorkTimeCorrections).AdminSubject);
    }

    [Fact]
    public async Task Correction_preserves_checklist_photos_and_previous_times()
    {
        var f = new Fixture();
        var form = QualityCycleForm.Create(f.Shift.Id, "Daily clean", f.Person.Id, "Cleaner One", Guid.NewGuid(), "Checklist", f.Start, new() { "Floor" }, f.Start.AddHours(2));
        form.Start(f.Start.AddMinutes(5)); form.Items[0].IsChecked = true;
        form.Photos.Add(new("photo", "Evidence")); form.CleanerNotes = "Original note"; form.Version = Guid.NewGuid();
        f.Forms.Setup(r => r.GetFormByShiftOccurrenceAsync(f.Shift.Id, f.Person.Id, f.Start, It.IsAny<CancellationToken>())).ReturnsAsync(form);
        Assert.IsType<ConflictObjectResult>(await f.Controller.Record(f.Body(), default));
        Assert.IsType<OkObjectResult>(await f.Controller.Record(f.Body(form.Version), default));
        Assert.True(form.Items[0].IsChecked); Assert.Single(form.Photos); Assert.Equal("Original note", form.CleanerNotes);
        // Entering both actual times intentionally ends this previously started visit.
        Assert.True(form.IsSubmitted); Assert.Equal(f.Start.AddHours(2), form.EndedAtUtc);
        Assert.Equal(f.Start.AddMinutes(5), Assert.Single(form.WorkTimeCorrections).PreviousStartUtc);
    }

    [Fact]
    public async Task Removed_work_record_is_a_visit_conflict_not_a_calendar_access_denial()
    {
        var f = new Fixture();
        var form = QualityCycleForm.Create(f.Shift.Id, "Daily clean", f.Person.Id, "Cleaner One", Guid.Empty, "Activities", f.Start, new());
        form.ShiftDeletedUtc = DateTime.UtcNow;
        f.Forms.Setup(r => r.GetFormByShiftOccurrenceAsync(f.Shift.Id, f.Person.Id, f.Start, It.IsAny<CancellationToken>())).ReturnsAsync(form);
        Assert.IsType<ConflictObjectResult>(await f.Controller.Record(f.Body(), default));
        Assert.True(f.Admin.CanUseShiftCalendar);
        f.Forms.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Partner_visit_is_an_explicit_readonly_conflict()
    {
        var f = new Fixture(); f.Shift.PartnerRequestId = Guid.NewGuid();
        Assert.IsType<ConflictObjectResult>(await f.Controller.Record(f.Body(), default));
        f.Forms.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Forms.Verify(r => r.CreateStartedFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_today_missing_reason_and_inverted_times()
    {
        var f = new Fixture();
        Assert.IsType<BadRequestObjectResult>(await f.Controller.Record(f.Body() with { OccurrenceStartUtc = DateTime.UtcNow }, default));
        Assert.IsType<BadRequestObjectResult>(await f.Controller.Record(f.Body() with { Reason = " " }, default));
        Assert.IsType<BadRequestObjectResult>(await f.Controller.Record(f.Body() with { ActualEndLocal = f.Start.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm") }, default));
    }

    [Fact]
    public async Task Calendar_uses_effective_visit_roster_instead_of_recurring_employee()
    {
        var f = new Fixture(); var replacement = new Employee { FirstName = "Replacement", LastName = "Cleaner" };
        f.Employees.Setup(e => e.ListAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { replacement, f.Person });
        var change = new ShiftOccurrenceAssignment { ShiftId = f.Shift.Id, OriginalStartUtc = f.Start,
            Mode = OccurrenceAssignmentMode.Replace, EmployeeIds = new() { replacement.Id } };
        change.Id = ShiftOccurrenceAssignment.Identity(f.Shift.Id, f.Start, null);
        f.Shifts.Setup(s => s.ListOccurrenceAssignmentsAsync(f.Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { change });
        var result = Assert.IsType<OkObjectResult>(await f.Controller.Get(f.Start.ToString("yyyy-MM-dd"), default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var assignee = Assert.Single(json.RootElement.GetProperty("occurrences").EnumerateArray()).GetProperty("assignees");
        Assert.Equal(replacement.Id.ToString(), Assert.Single(assignee.EnumerateArray()).GetProperty("employeeId").GetString());
    }

    [Fact]
    public async Task Cancelled_and_other_tenant_visits_cannot_be_recorded()
    {
        var f = new Fixture();
        var cancelled = ShiftOccurrenceException.Create(f.Shift.Id, f.Start, f.Start, f.Start.AddHours(2), ShiftOccurrenceExceptionStatus.CustomerCancelled, "Cancelled");
        f.Shifts.Setup(s => s.ListOccurrenceExceptionsAsync(f.Shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { cancelled });
        Assert.IsType<ConflictObjectResult>(await f.Controller.Record(f.Body() with { ExceptionId = cancelled.Id }, default));
        f.Shifts.Setup(s => s.GetAsync(f.Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);
        Assert.IsType<BadRequestObjectResult>(await f.Controller.Record(f.Body(), default));
    }

    [Fact]
    public async Task Employee_page_permission_update_can_grant_and_revoke_but_not_enable_non_admins()
    {
        var employees = new Mock<IEmployeeRepository>();
        var person = new Employee { Role = Employee.RoleAdmin, Email = "other@hiava.fi" };
        employees.Setup(e => e.GetByIdAsync(person.Id, It.IsAny<CancellationToken>())).ReturnsAsync(person);
        var controller = new EmployeesAdminController(null!);
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "granting-admin") }, "test")) } };
        Assert.IsType<OkObjectResult>(await controller.CalendarAccess(person.Id, new(true), employees.Object, default));
        Assert.True(person.CanUseShiftCalendar);
        Assert.IsType<OkObjectResult>(await controller.CalendarAccess(person.Id, new(false), employees.Object, default));
        Assert.False(person.CanUseShiftCalendar);
        person.Role = Employee.RoleManager;
        Assert.IsType<BadRequestObjectResult>(await controller.CalendarAccess(person.Id, new(true), employees.Object, default));
        employees.Verify(e => e.SaveAsync(person, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(new[] { true, false }, person.ShiftCalendarAccessChanges.Select(change => change.Enabled));
        Assert.All(person.ShiftCalendarAccessChanges, change => Assert.Equal("granting-admin", change.AdminSubject));
    }
}
