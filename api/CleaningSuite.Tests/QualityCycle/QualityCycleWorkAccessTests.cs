using CleaningSuite.Api.Controllers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using Moq;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleWorkAccessTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Start.AddDays(1)); }
    private sealed class Setup
    {
        public readonly Employee Employee = new() { IsActive = true };
        public readonly Shift Shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily", new ShiftSchedule
            { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, Start.Date, null, null);
        public readonly Mock<IEmployeeRepository> Employees = new();
        public readonly Mock<IShiftRepository> Shifts = new();
        public readonly Mock<IQualityCycleRepository> Forms = new();
        public readonly Mock<ITenantContext> Tenant = new();
        public readonly Mock<ITenantProfileRepository> Profiles = new();
        public readonly QualityCycleForm Form;
        public readonly List<ShiftAssignment> Roster = [];
        public Setup(int stage = 0)
        {
            Tenant.SetupGet(x => x.TenantId).Returns("synthetic");
            Profiles.Setup(x => x.GetAsync("synthetic", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "UTC" });
            Form = QualityCycleForm.Create(Shift.Id, Shift.Name, Employee.Id, "Original cleaner", Guid.NewGuid(), "Original checklist", Start, ["Optional task"], Start.AddHours(2));
            if (stage > 0) Form.Start(Start.AddMinutes(12));
            if (stage > 1) Form.Submit([new() { ItemText = "Optional task", IsChecked = false }], null, "History", [new("/synthetic.png", "Original photo")], Start.AddHours(1));
            var assignment = ShiftAssignment.Create(Shift.Id, Employee.Id, null); assignment.AssignedAtUtc = Start.AddDays(-1); Roster.Add(assignment);
            Employees.Setup(x => x.GetByIdAsync(Employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => Employee);
            Shifts.Setup(x => x.GetAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => Shift);
            Shifts.Setup(x => x.ListAssignmentHistoryAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => Roster);
            Forms.Setup(x => x.GetFormByTokenAsync(Form.Token, It.IsAny<CancellationToken>())).ReturnsAsync(Form);
            Forms.Setup(x => x.GetFormAsync(Form.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Form);
            Forms.Setup(x => x.GetFormByShiftOccurrenceAsync(Shift.Id, Employee.Id, Start, It.IsAny<CancellationToken>())).ReturnsAsync(Form);
            Forms.Setup(x => x.ListFormsAsync(null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([Form]);
        }
        public Task<QualityCycleFormDto> Read() => new QualityCycleHandlers.GetQualityCycleFormByTokenQueryHandler(Forms.Object, Shifts.Object, Employees.Object, WorkAccessFixtures.Reservation, Profiles.Object, Tenant.Object).Handle(new(Form.Token), default);
        public Task<QualityCycleFormDto> Submit() => new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(Forms.Object, NoopUploadContentValidator.Instance, Shifts.Object, Employees.Object, WorkAccessFixtures.Reservation, new Clock(), Profiles.Object, Tenant.Object).Handle(new(Form.Token, [new("Optional task", false)], CleanerNotes: "Finished", Photos: [new("/new.png", "New photo")]), default);
        public Task<QualityCycleFormDto> End(Guid? employee = null) => new QualityCycleHandlers.EndQualityCycleFormCommandHandler(Forms.Object, NoopUploadContentValidator.Instance, Shifts.Object, Employees.Object, WorkAccessFixtures.Reservation, new Clock(), Profiles.Object, Tenant.Object).Handle(new(Form.Id, employee ?? Employee.Id, [new("Optional task", false)]), default);
        public Task<QualityCycleFormDto> StartWork() => new QualityCycleHandlers.StartQualityCycleFormCommandHandler(Shifts.Object, Employees.Object, Forms.Object, WorkAccessFixtures.Reservation, new Clock(), Profiles.Object, Tenant.Object).Handle(new(Shift.Id, Employee.Id, Start, Start.AddHours(2)), default);
        public void NoWrite() => Forms.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task Missing_or_inactive_owner_denied_all_work_paths_even_after_start_or_submission(int stage, bool missing)
    {
        var s = new Setup(stage); s.Employee.IsActive = false;
        if (missing) s.Employees.Setup(x => x.GetByIdAsync(s.Employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(s.Read);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(s.Submit);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.End());
        if (missing) await Assert.ThrowsAsync<NotFoundException>(s.StartWork);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(s.StartWork);
        var personal = new QualityCycleHandlers.GetMyShiftsQueryHandler(s.Shifts.Object, s.Forms.Object, s.Employees.Object, WorkAccessFixtures.Reservation);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => personal.Handle(new(s.Employee.Id, Start.Date, Start.Date.AddDays(1)), default));
        s.NoWrite();
    }

    [Theory]
    [InlineData(0, "inactive")] [InlineData(1, "inactive")] [InlineData(2, "inactive")]
    [InlineData(0, "missing")] [InlineData(1, "missing")] [InlineData(2, "missing")]
    [InlineData(0, "deleted-form")] [InlineData(1, "deleted-form")] [InlineData(2, "deleted-form")]
    public async Task Unavailable_shift_or_form_denies_token_and_mutations_but_preserves_admin_history(int stage, string state)
    {
        var s = new Setup(stage);
        if (state == "inactive") s.Shift.IsActive = false;
        if (state == "missing") s.Shifts.Setup(x => x.GetAsync(s.Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);
        if (state == "deleted-form") s.Form.ShiftDeletedUtc = Start.AddHours(3);
        await Assert.ThrowsAsync<QualityCycleConflictException>(s.Read);
        await Assert.ThrowsAsync<QualityCycleConflictException>(s.Submit);
        await Assert.ThrowsAsync<QualityCycleConflictException>(() => s.End());
        var history = await new QualityCycleHandlers.ListQualityCycleFormsQueryHandler(s.Forms.Object).Handle(new(), default);
        Assert.Equal(s.Form.Id, Assert.Single(history).Id);
        if (stage == 2) Assert.Equal("Original photo", Assert.Single(history[0].Photos).Note);
        s.NoWrite();
    }

    [Fact]
    public async Task Removed_unstarted_owner_denied_read_start_and_submit()
    {
        var s = new Setup(); s.Roster.Clear();
        await Assert.ThrowsAsync<QualityCycleConflictException>(s.Read);
        await Assert.ThrowsAsync<QualityCycleConflictException>(s.Submit);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(s.StartWork);
        s.NoWrite(); Assert.Null(s.Form.StartedAtUtc);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Active_original_owner_can_finish_after_roster_change_without_rewriting_snapshot(bool publicToken)
    {
        var s = new Setup(1); s.Roster.Clear();
        var before = await s.Read();
        Assert.Equal("Original cleaner", before.EmployeeName); Assert.Equal("Original checklist", before.TemplateTitle);
        var finished = publicToken ? await s.Submit() : await s.End();
        Assert.Equal(before.StartedAtUtc, finished.StartedAtUtc); Assert.Equal(Start, finished.ShiftOccurrenceUtc);
        Assert.Equal(s.Employee.Id, finished.EmployeeId); Assert.False(Assert.Single(finished.Items).IsChecked);
        if (publicToken) Assert.Equal("New photo", Assert.Single(finished.Photos).Note);
        await Assert.ThrowsAsync<QualityCycleConflictException>(s.Submit);
        await Assert.ThrowsAsync<QualityCycleConflictException>(() => s.End());
        Assert.Equal(finished.SubmittedUtc, (await s.Read()).SubmittedUtc);
    }

    [Fact]
    public async Task Another_authenticated_employee_cannot_end_original_owners_work()
    {
        var s = new Setup(1); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.End(Guid.NewGuid())); s.NoWrite();
    }

    [Fact]
    public async Task Unstarted_current_roster_can_submit_past_work_with_optional_items()
    {
        var s = new Setup(); Assert.Equal(s.Form.Id, (await s.Read()).Id);
        Assert.True((await s.Submit()).IsSubmitted); Assert.Null(s.Form.StartedAtUtc);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Public_task_responses_disable_caching_and_route_token_is_authoritative(bool submit)
    {
        var s = new Setup(1); var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetQualityCycleFormByTokenQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(QualityCycleHandlers.MapForm(s.Form));
        mediator.Setup(x => x.Send(It.IsAny<SubmitQualityCycleFormCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(QualityCycleHandlers.MapForm(s.Form));
        var controller = new QualityCyclePublicController(mediator.Object)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        if (submit)
        {
            await controller.Submit(s.Form.Token, new("untrusted-body-token", []), default);
            mediator.Verify(x => x.Send(It.Is<SubmitQualityCycleFormCommand>(c => c.Token == s.Form.Token), It.IsAny<CancellationToken>()), Times.Once);
        }
        else await controller.GetByToken(s.Form.Token, default);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", controller.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal("noindex, nofollow", controller.Response.Headers["X-Robots-Tag"].ToString());
    }

}
