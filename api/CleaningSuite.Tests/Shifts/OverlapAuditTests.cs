using System;
using System.Linq;
using CleaningSuite.Application.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class OverlapAuditTests
{
    private static readonly DateTime Day = new(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc);
    private static ShiftOverlapVisit Visit(string name, int fromHour, int toHour) =>
        new(Guid.NewGuid(), name, Day.AddHours(fromHour), Day.AddHours(toHour));

    [Fact]
    public void Two_overlapping_visits_for_one_cleaner_are_reported_with_both_shifts()
    {
        var employee = Guid.NewGuid();
        var morning = Visit("Morning", 0, 2); var evening = Visit("Evening", 1, 3);
        var findings = OverlapAudit.Find([(employee, morning), (employee, evening)]);
        var finding = Assert.Single(findings);
        Assert.Equal(employee, finding.EmployeeId);
        Assert.Equal(morning, finding.Earlier);
        Assert.Equal(evening, finding.Later);
    }

    [Fact]
    public void Back_to_back_visits_are_not_overlaps()
    {
        var employee = Guid.NewGuid();
        Assert.Empty(OverlapAudit.Find([(employee, Visit("A", 0, 2)), (employee, Visit("B", 2, 4))]));
    }

    [Fact]
    public void Same_time_visits_for_different_cleaners_are_not_overlaps()
    {
        Assert.Empty(OverlapAudit.Find([(Guid.NewGuid(), Visit("A", 0, 2)), (Guid.NewGuid(), Visit("B", 0, 2))]));
    }

    [Fact]
    public void Every_later_visit_is_reported_against_the_latest_ending_earlier_visit()
    {
        var employee = Guid.NewGuid();
        var long1 = Visit("Long", 0, 6); var short1 = Visit("Short", 1, 2); var third = Visit("Third", 3, 4);
        var findings = OverlapAudit.Find([(employee, long1), (employee, short1), (employee, third)]);
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(long1, f.Earlier));
        Assert.Equal(new[] { short1, third }, findings.Select(f => f.Later).ToArray());
    }
}
