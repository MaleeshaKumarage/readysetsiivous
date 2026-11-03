using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class ShiftCompletionListTests
{
    private static ShiftSchedule OneTime() => new()
    {
        Type = ShiftScheduleType.OneTime,
        OneTimeDate = new DateTime(2026, 11, 2),
        OneTimeStart = new TimeSpan(9, 0, 0),
        OneTimeEnd = new TimeSpan(11, 0, 0),
    };

    private static ShiftSchedule Weekly() => new()
    {
        Type = ShiftScheduleType.Weekly,
        WeeklyDay = DayOfWeek.Monday,
        WeeklyStart = new TimeSpan(9, 0, 0),
        WeeklyEnd = new TimeSpan(11, 0, 0),
    };

    [Fact]
    public async Task OneTimeShift_WithSubmittedChecklist_IsCompleted_AndOpenOnesAreNot()
    {
        var companyId = Guid.NewGuid();
        var done = Shift.Create(companyId, Guid.NewGuid(), "Done visit", OneTime(), null, null, null);
        var open = Shift.Create(companyId, Guid.NewGuid(), "Open visit", OneTime(), null, null, null);
        var recurring = Shift.Create(companyId, Guid.NewGuid(), "Weekly", Weekly(), null, null, null);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync([done, open, recurring]);
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(r => r.ListFormsAsync(done.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new QualityCycleForm { ShiftId = done.Id, IsSubmitted = true }]);
        forms.Setup(r => r.ListFormsAsync(open.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new QualityCycleForm { ShiftId = open.Id, IsSubmitted = false }]);

        var list = await new ShiftHandlers.ListShiftsQueryHandler(shifts.Object, forms.Object)
            .Handle(new ListShiftsQuery(null, null), default);

        Assert.True(list.Single(s => s.Id == done.Id).IsCompleted);
        Assert.False(list.Single(s => s.Id == open.Id).IsCompleted);
        Assert.False(list.Single(s => s.Id == recurring.Id).IsCompleted);
        forms.Verify(r => r.ListFormsAsync(recurring.Id, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletedChecklist_DoesNotCompleteVisit()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Visit", OneTime(), null, null, null);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(r => r.ListFormsAsync(shift.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new QualityCycleForm { ShiftId = shift.Id, IsSubmitted = true, ShiftDeletedUtc = DateTime.UtcNow }]);

        var list = await new ShiftHandlers.ListShiftsQueryHandler(shifts.Object, forms.Object).Handle(new ListShiftsQuery(null, null), default);

        Assert.False(list.Single().IsCompleted);
    }
}
