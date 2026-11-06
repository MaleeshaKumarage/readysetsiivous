using System;
using System.Linq;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class OneTimeAssignmentRosterTests
{
    [Fact]
    public void A_one_time_visit_is_offered_once_to_an_assigned_cleaner()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
        var day = new DateTime(2030, 1, 7);
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Once",
            new ShiftSchedule { Type = ShiftScheduleType.OneTime, OneTimeDate = day, OneTimeStart = TimeSpan.FromHours(8), OneTimeEnd = TimeSpan.FromHours(10) },
            null, day, day, null);
        var cleaner = Guid.NewGuid();
        var from = new DateTime(2030, 1, 6, 22, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2030, 1, 8, 0, 0, 0, DateTimeKind.Utc);

        var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, from, to, zone);
        Assert.Single(occurrences);

        var assignment = new ShiftAssignment { ShiftId = shift.Id, EmployeeId = cleaner, IsActive = true, AssignedAtUtc = DateTime.UtcNow.AddDays(-1) };
        var roster = ShiftVisitRoster.Resolve(shift.Id, occurrences, Array.Empty<ShiftOccurrenceException>(),
            new[] { assignment }, Array.Empty<ShiftOccurrenceAssignment>(), false, null);
        var forCleaner = roster.Where(entry => entry.EmployeeIds.Contains(cleaner)).ToArray();
        Assert.Single(forCleaner);
    }
}
