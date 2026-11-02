using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;

namespace CleaningSuite.Tests;

public class PushScheduleRecipientTests
{
    [Fact]
    public void Old_one_visit_assignees_are_excluded_but_future_and_moved_owners_remain()
    {
        var now = DateTime.UtcNow;
        var former = Guid.NewGuid(); var future = Guid.NewGuid(); var moved = Guid.NewGuid(); var extra = Guid.NewGuid();
        var movedException = new ShiftOccurrenceException { OriginalStartUtc = now.AddDays(-7), StartUtc = now.AddDays(2), EndUtc = now.AddDays(2).AddHours(1) };
        var extraException = new ShiftOccurrenceException { StartUtc = now.AddDays(1), EndUtc = now.AddDays(1).AddHours(1), EmployeeId = extra };
        var overrides = new[] {
            new ShiftOccurrenceAssignment { OriginalStartUtc = now.AddDays(-10), Mode = OccurrenceAssignmentMode.Replace, EmployeeIds = [former] },
            new ShiftOccurrenceAssignment { OriginalStartUtc = now.AddDays(3), Mode = OccurrenceAssignmentMode.Replace, EmployeeIds = [future] },
            new ShiftOccurrenceAssignment { OriginalStartUtc = movedException.OriginalStartUtc, Mode = OccurrenceAssignmentMode.Replace, EmployeeIds = [moved] }
        };
        var recipients = ShiftRepository.FutureRecipients([], overrides, [movedException, extraException], now);
        Assert.DoesNotContain(former, recipients);
        Assert.Contains(future, recipients); Assert.Contains(moved, recipients); Assert.Contains(extra, recipients);
    }

    [Fact]
    public void Ended_recurring_history_does_not_receive_shift_changes()
    {
        var now = DateTime.UtcNow; var former = Guid.NewGuid(); var current = Guid.NewGuid();
        var recipients = ShiftRepository.FutureRecipients([
            new ShiftAssignment { EmployeeId = former, IsActive = false, Periods = [new() { FromUtc = now.AddDays(-10), ToUtc = now.AddDays(-2) }] },
            new ShiftAssignment { EmployeeId = current, IsActive = true }
        ], [], [], now);
        Assert.Equal([current], recipients);
    }
}
