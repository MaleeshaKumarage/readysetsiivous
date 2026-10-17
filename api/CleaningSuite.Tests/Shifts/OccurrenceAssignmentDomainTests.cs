using CleaningSuite.Domain.Shifts;
using CleaningSuite.Application.Shifts;

namespace CleaningSuite.Tests.Shifts;

public class OccurrenceAssignmentDomainTests
{
    [Fact]
    public void One_visit_replacement_extra_and_restore_preserve_other_visits_and_schedule_changes()
    {
        var shift = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var legacy = Guid.NewGuid();
        var time = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
        var regular = new ShiftAssignment { ShiftId = shift, EmployeeId = a, IsActive = true, AssignedAtUtc = time.AddDays(-1) };
        var generated = new[] { new ShiftOccurrence { StartUtc = time, EndUtc = time.AddHours(2) },
            new ShiftOccurrence { StartUtc = time.AddDays(1), EndUtc = time.AddDays(1).AddHours(2) } };
        var move = ShiftOccurrenceException.Create(shift, time, time.AddHours(1), time.AddHours(3), ShiftOccurrenceExceptionStatus.Moved, "Move", legacy);
        var change = ShiftOccurrenceAssignment.Create(shift, time, null, OccurrenceAssignmentMode.Replace, [b], "Cover");
        var replaced = ShiftVisitRoster.Resolve(shift, generated, [move], [regular], [change]);
        Assert.Equal(b, Assert.Single(replaced[0].EmployeeIds)); Assert.Equal(time.AddHours(1), replaced[0].Occurrence.StartUtc);
        Assert.Equal(a, Assert.Single(replaced[1].EmployeeIds));
        change.Update(OccurrenceAssignmentMode.Add, [a, b], "Team");
        Assert.Equal(2, ShiftVisitRoster.Resolve(shift, generated, [move], [regular], [change])[0].EmployeeIds.Count);
        change.Update(OccurrenceAssignmentMode.Inherit, [], "Restore");
        var restored = ShiftVisitRoster.Resolve(shift, generated, [move], [regular], [change]);
        Assert.Equal(a, Assert.Single(restored[0].EmployeeIds));
        Assert.Equal(ShiftOccurrenceExceptionStatus.Moved, restored[0].Occurrence.Status);
    }

    [Fact]
    public void Scheduled_and_extra_visits_have_stable_distinct_roster_identities()
    {
        var shift = Guid.NewGuid(); var time = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(ShiftOccurrenceAssignment.Identity(shift, time, null),
            ShiftOccurrenceAssignment.Identity(shift, DateTime.SpecifyKind(time, DateTimeKind.Unspecified), null));
        Assert.NotEqual(ShiftOccurrenceAssignment.Identity(shift, time, null), ShiftOccurrenceAssignment.Identity(shift, null, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ShiftOccurrenceAssignment.Identity(shift, null, null));
        Assert.Throws<ArgumentException>(() => ShiftOccurrenceAssignment.Identity(shift, time, Guid.NewGuid()));
    }

    [Fact]
    public void Team_overrides_deduplicate_and_inherit_is_an_explicit_marker()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var item = ShiftOccurrenceAssignment.Create(Guid.NewGuid(), DateTime.UtcNow, null, OccurrenceAssignmentMode.Add, [a, b, a], "Cover visit");
        Assert.Equal(2, item.EmployeeIds.Count);
        item.Update(OccurrenceAssignmentMode.Inherit, [], "Restore recurring roster");
        Assert.Equal(OccurrenceAssignmentMode.Inherit, item.Mode); Assert.Empty(item.EmployeeIds);
        Assert.Throws<ArgumentException>(() => item.Update(OccurrenceAssignmentMode.Replace, [], "Replace"));
        Assert.Throws<ArgumentException>(() => item.Update(OccurrenceAssignmentMode.Add, [Guid.Empty], "Add"));
    }

    [Fact]
    public void Midnight_handover_uses_visit_start_so_an_overnight_visit_is_not_given_to_both_employees()
    {
        var boundary = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        var outgoing = new ShiftAssignment { IsActive = true, AssignedAtUtc = boundary.AddDays(-10) };
        outgoing.EndFrom(boundary, boundary.AddHours(1));
        var incoming = new ShiftAssignment { AssignedAtUtc = boundary.AddDays(-1) };
        incoming.BeginFrom(boundary, boundary.AddHours(-1), "Handover");
        Assert.True(outgoing.CoversVisit(boundary.AddHours(-1), boundary.AddHours(1)));
        Assert.False(incoming.CoversVisit(boundary.AddHours(-1), boundary.AddHours(1)));
        Assert.False(outgoing.CoversVisit(boundary, boundary.AddHours(2)));
        Assert.True(incoming.CoversVisit(boundary, boundary.AddHours(2)));
    }

    [Fact]
    public void Replacement_truncates_future_periods_without_rewriting_earlier_history()
    {
        var boundary = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        var assignment = new ShiftAssignment { IsActive = true, Periods = [
            new() { FromUtc = boundary.AddDays(-10), ToUtc = boundary.AddDays(-5) },
            new() { FromUtc = boundary.AddDays(-2) }, new() { FromUtc = boundary.AddDays(5) }] };
        assignment.EndFrom(boundary, boundary.AddDays(-1));
        Assert.Equal(2, assignment.Periods.Count);
        Assert.Equal(boundary.AddDays(-5), assignment.Periods[0].ToUtc);
        Assert.Equal(boundary, assignment.Periods[1].ToUtc);
        Assert.True(assignment.IsActive);
    }
}
