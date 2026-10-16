using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class ShiftOccurrenceExceptionTests
{
    [Fact]
    public void Scheduled_exception_requires_reason_and_has_stable_identity()
    {
        var shiftId = Guid.NewGuid();
        var original = new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc);
        var first = ShiftOccurrenceException.Create(shiftId, original, original.AddHours(1), original.AddHours(3),
            ShiftOccurrenceExceptionStatus.Moved, "Customer requested a later visit");
        var retry = ShiftOccurrenceException.Create(shiftId, original, original.AddHours(1), original.AddHours(3),
            ShiftOccurrenceExceptionStatus.Moved, "Customer requested a later visit");

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal("Customer requested a later visit", first.Reason);
        Assert.Throws<ArgumentException>(() => ShiftOccurrenceException.Create(shiftId, original,
            original, original.AddHours(1), ShiftOccurrenceExceptionStatus.Moved, " "));
    }

    [Fact]
    public void Extra_visit_cannot_replace_a_generated_occurrence()
    {
        var start = new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => ShiftOccurrenceException.Create(Guid.NewGuid(), start,
            start, start.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Extra service"));
    }

    [Fact]
    public async Task Save_handler_rejects_an_original_occurrence_that_is_not_in_the_schedule()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Weekday",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) },
            null, null, null);
        var shifts = new Mock<IShiftRepository>(); var employees = new Mock<IEmployeeRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        var handler = new ShiftHandlers.SaveShiftOccurrenceExceptionCommandHandler(shifts.Object, employees.Object);
        var missing = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<ShiftConflictException>(() => handler.Handle(new(shift.Id, null, missing,
            missing, missing.AddHours(2), ShiftOccurrenceExceptionStatus.Moved, "Move"), CancellationToken.None));
        shifts.Verify(x => x.SaveOccurrenceExceptionAsync(It.IsAny<ShiftOccurrenceException>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Occurrence_query_merges_move_cancellation_and_extra_visit()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) },
            null, null, null);
        var day = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        var original = day.AddHours(8);
        var moved = ShiftOccurrenceException.Create(shift.Id, original, day.AddHours(12), day.AddHours(14),
            ShiftOccurrenceExceptionStatus.Moved, "Customer request");
        var extra = ShiftOccurrenceException.Create(shift.Id, null, day.AddHours(15), day.AddHours(16),
            ShiftOccurrenceExceptionStatus.Extra, "Deep clean");
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shifts.Setup(x => x.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { moved, extra });

        var result = await new ShiftHandlers.GetShiftOccurrencesQueryHandler(shifts.Object)
            .Handle(new(shift.Id, day, day), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.ExceptionId == moved.Id && x.StartUtc == day.AddHours(12));
        Assert.Contains(result, x => x.ExceptionId == extra.Id && x.Status == ShiftOccurrenceExceptionStatus.Extra);
    }

    [Fact]
    public void Resolver_includes_a_moved_visit_when_only_its_new_date_is_in_the_window()
    {
        var original = new DateTime(2026, 11, 30, 8, 0, 0, DateTimeKind.Utc);
        var moved = ShiftOccurrenceException.Create(Guid.NewGuid(), original,
            new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 2, 10, 0, 0, DateTimeKind.Utc),
            ShiftOccurrenceExceptionStatus.Moved, "Customer closure");

        var result = ShiftOccurrenceResolver.Apply(Array.Empty<ShiftOccurrence>(), new[] { moved });

        var occurrence = Assert.Single(result);
        Assert.Equal(moved.StartUtc, occurrence.StartUtc);
        Assert.Equal(original, occurrence.OriginalStartUtc);
    }
}
