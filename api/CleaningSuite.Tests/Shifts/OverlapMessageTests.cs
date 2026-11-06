using CleaningSuite.Application.Shifts;

namespace CleaningSuite.Tests.Shifts;

public class OverlapMessageTests
{
    private static readonly TimeZoneInfo Helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

    private static ShiftVisitRosterEntry Visit(DateTime startUtc, DateTime endUtc) =>
        new(new ResolvedShiftOccurrence(startUtc, endUtc), null, null, [], "legacy", null);

    [Fact]
    public void Overlap_message_names_the_clashing_shift_and_its_local_time()
    {
        var requested = Visit(new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 11, 0, 0, DateTimeKind.Utc));
        var clashing = Visit(new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc));

        var message = VisitAssignmentSafety.OverlapMessage(requested, clashing, "QA Specific days Branch B", Helsinki);

        Assert.Contains("It clashes with \"QA Specific days Branch B\", 6 Nov 2026 10:00 to 6 Nov 2026 12:00.", message);
        Assert.Contains("No assignments were saved.", message);
    }

    [Fact]
    public void Overlap_message_without_a_clashing_visit_does_not_name_one()
    {
        var requested = Visit(new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 11, 0, 0, DateTimeKind.Utc));

        var message = VisitAssignmentSafety.OverlapMessage(requested, null, null, Helsinki);

        Assert.DoesNotContain("clashes with", message);
    }
}
