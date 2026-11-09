using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests.Shifts;

// The Occurrences panel and the admin calendar must list the same visits. The calendar lists only active shifts.
public class ShiftOccurrencePanelParityTests
{
    [Fact]
    public async Task Inactive_shift_lists_no_occurrences_so_the_panel_matches_the_calendar()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Jukankatu", new ShiftSchedule {
            Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(14), DailyEnd = TimeSpan.FromHours(16) },
            null, new DateOnly(2026, 11, 1).ToDateTime(TimeOnly.MinValue), null, null);
        shift.IsActive = false;
        var repository = new Mock<IShiftRepository>();
        repository.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        var handler = new ShiftHandlers.GetShiftOccurrencesQueryHandler(repository.Object);

        var result = await handler.Handle(new GetShiftOccurrencesQuery(shift.Id,
            new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 23, 59, 59, DateTimeKind.Utc)), default);

        Assert.Empty(result);
    }
}
