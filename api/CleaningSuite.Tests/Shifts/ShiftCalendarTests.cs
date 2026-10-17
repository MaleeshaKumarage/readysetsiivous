using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class ShiftCalendarTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Shift_deletion_requires_deactivation_but_does_not_block_on_roster_history(bool active)
    {
        var shift = new Shift { Id = Guid.NewGuid(), IsActive = active };
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShiftAssignment { ShiftId = shift.Id, EmployeeId = Guid.NewGuid() }]);
        var handler = new ShiftHandlers.DeleteShiftCommandHandler(shifts.Object);
        if (active) await Assert.ThrowsAsync<CleaningSuite.Application.Common.InactiveRecordDeleteConflictException>(() => handler.Handle(new(shift.Id), default));
        else await handler.Handle(new(shift.Id), default);
        shifts.Verify(x => x.DeleteAsync(shift.Id, It.IsAny<CancellationToken>()), active ? Times.Never() : Times.Once());
    }
    [Fact]
    public async Task Detached_checklist_cannot_be_submitted_using_an_old_public_token()
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Deleted shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Activities", DateTime.UtcNow.AddHours(-2), ["Corridor"]);
        form.ShiftDeletedUtc = DateTime.UtcNow;
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(x => x.GetFormByTokenAsync(form.Token, It.IsAny<CancellationToken>())).ReturnsAsync(form);
        var handler = new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(forms.Object,
            Mock.Of<CleaningSuite.Application.Common.IUploadContentValidator>());
        await Assert.ThrowsAsync<QualityCycleConflictException>(() => handler.Handle(new(form.Token, [new("Corridor", true)], null, "Note"), default));
        forms.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(form.IsSubmitted);
    }
    [Fact]
    public void Calendar_uses_tenant_midnights_across_DST_and_contiguous_history_pages()
    {
        var zone = TenantTimeZone.Resolve("Europe/Helsinki");
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var today = ShiftCalendarWindow.Create("today", null, now, zone);
        Assert.Equal("2026-10-25", today.FromLocalDate);
        Assert.Equal(TimeSpan.FromHours(25), today.ToUtc - today.FromUtc);
        var past = ShiftCalendarWindow.Create("past", null, now, zone);
        var older = ShiftCalendarWindow.Create("past", past.FromLocalDate, now, zone);
        Assert.Equal(today.FromUtc, past.ToUtc);
        Assert.Equal(past.FromUtc, older.ToUtc);
        var future = ShiftCalendarWindow.Create("future", null, now, zone);
        var later = ShiftCalendarWindow.Create("future", future.ToLocalDate, now, zone);
        Assert.Equal(today.ToUtc, future.FromUtc);
        Assert.Equal(future.ToUtc, later.FromUtc);
        Assert.All(new[] { past, older, future, later }, window => Assert.InRange((window.ToUtc - window.FromUtc).TotalDays, 30, 32));
    }

    [Theory]
    [InlineData("invalid", null)] [InlineData("today", "2026-10-01")]
    [InlineData("past", "2026-11-01")] [InlineData("future", "2026-10-01")]
    [InlineData("past", "not-a-date")] [InlineData("past", "0001-01-01")]
    public void Calendar_rejects_invalid_view_or_cursor(string view, string? cursor) =>
        Assert.Throws<ValidationException>(() => ShiftCalendarWindow.Create(view, cursor,
            new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc));

    [Fact]
    public async Task Own_completed_work_survives_deleted_shift_and_roster_changes_without_leaking_other_employees()
    {
        var employee = Guid.NewGuid();
        var from = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListAssignmentsByEmployeeAsync(employee, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftAssignment>());
        var recorded = QualityCycleForm.Create(Guid.NewGuid(), "Historic corridor shift", employee, "Cleaner", Guid.NewGuid(), "Activities", from.AddHours(8), ["Corridor"], from.AddHours(10));
        recorded.IsSubmitted = true; recorded.Items[0].IsChecked = true;
        recorded.ShiftDeletedUtc = from.AddDays(1);
        recorded.Photos = [new("data:image/png;base64,history", "Corridor cleaned")];
        var foreign = QualityCycleForm.Create(Guid.NewGuid(), "Private", Guid.NewGuid(), "Other", Guid.NewGuid(), "Activities", from.AddHours(9), ["Private"]);
        foreign.IsSubmitted = true;
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(x => x.ListFormsAsync(null, from, from.AddDays(1), It.IsAny<CancellationToken>(), employee, null, null, null))
            .ReturnsAsync(new[] { recorded, foreign });
        var result = await new QualityCycleHandlers.GetMyShiftsQueryHandler(shifts.Object, forms.Object)
            .Handle(new(employee, from, from.AddDays(1)), default);
        var own = Assert.Single(result);
        Assert.True(own.IsDeleted); Assert.Equal(recorded.ShiftName, own.ShiftName);
        Assert.True(own.Form!.IsSubmitted);
        Assert.Equal("Corridor cleaned", Assert.Single(own.Form.Photos!).Note);
    }
}
