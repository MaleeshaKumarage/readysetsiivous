using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class ShiftValidityWindowTests
{
    private static Shift WeeklyShift(DateTime? validFrom, DateTime? validUntil) => Shift.Create(
        Guid.NewGuid(), Guid.NewGuid(), "Weekly clean",
        new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Monday], WeeklyStart = new(9, 0, 0), WeeklyEnd = new(10, 0, 0) },
        null, validFrom, validUntil);

    private static Mock<IShiftRepository> EmptyRepository()
    {
        var repository = new Mock<IShiftRepository>();
        repository.Setup(x => x.ListOccurrenceExceptionsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceException>());
        repository.Setup(x => x.ListAssignmentHistoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftAssignment>());
        repository.Setup(x => x.ListOccurrenceAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceAssignment>());
        return repository;
    }

    [Fact]
    public async Task Roster_never_schedules_visits_after_the_shift_valid_until()
    {
        var zone = TenantTimeZone.Resolve("Europe/Helsinki");
        var shift = WeeklyShift(new(2026, 10, 1), new(2026, 10, 31));
        var repository = EmptyRepository();
        var november = await repository.Object.ResolveRosterAsync(shift,
            new DateTime(2026, 10, 31, 22, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 30, 22, 0, 0, DateTimeKind.Utc), zone, default);
        Assert.Empty(november);
    }

    [Fact]
    public async Task Roster_keeps_every_visit_inside_the_window_and_none_after_it()
    {
        var zone = TenantTimeZone.Resolve("Europe/Helsinki");
        var shift = WeeklyShift(new(2026, 10, 1), new(2026, 10, 31));
        var roster = await EmptyRepository().Object.ResolveRosterAsync(shift,
            new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 30, 22, 0, 0, DateTimeKind.Utc), zone, default);
        // Mondays 5, 12, 19 and 26 October 2026, at 09:00 Helsinki time (UTC+3 before the 25 October clock change).
        Assert.Equal(new[] { 5, 12, 19, 26 }, roster.Select(x => TenantTimeZone.LocalDate(x.Occurrence.StartUtc, zone).Day).ToArray());
    }
}
