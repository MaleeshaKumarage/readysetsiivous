using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Tests.WorkQueue;

public class WorkQueueRulesTests
{
    [Theory]
    [InlineData(true, WorkQueueViews.NeedsAction, true, WorkQueueViews.NeedsAction)]
    [InlineData(false, WorkQueueViews.NeedsAction, true, WorkQueueViews.Cancelled)]
    [InlineData(false, WorkQueueViews.Scheduled, true, WorkQueueViews.Cancelled)]
    [InlineData(false, WorkQueueViews.Scheduled, false, WorkQueueViews.Scheduled)]
    [InlineData(false, WorkQueueViews.InProgress, true, WorkQueueViews.InProgress)]
    [InlineData(false, WorkQueueViews.Completed, true, WorkQueueViews.Completed)]
    public void Deactivated_shift_future_visits_are_cancelled_and_past_visits_keep_their_view(bool shiftIsActive, string view, bool future, string expected)
    {
        var now = new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);
        var start = future ? now.AddDays(1) : now.AddDays(-1);

        Assert.Equal(expected, WorkQueueRules.ActiveShiftView(shiftIsActive, view, start, now));
    }

    [Theory]
    [InlineData(Booking.StatusNew, false, WorkQueueViews.NeedsAction, "New booking")]
    [InlineData(Booking.StatusConfirmed, false, WorkQueueViews.NeedsAction, "Unassigned")]
    [InlineData(Booking.StatusConfirmed, true, WorkQueueViews.Scheduled, "Confirmed")]
    [InlineData(Booking.StatusCompleted, true, WorkQueueViews.Completed, "Completed")]
    [InlineData(Booking.StatusCancelled, false, WorkQueueViews.Cancelled, "Cancelled")]
    public void Booking_ClassifiesIntoExactlyOneView(string status, bool assigned, string expectedView, string expectedLabel)
    {
        var booking = new Booking { Status = status, EmployeeId = assigned ? "employee-1" : null };

        var matches = WorkQueueViews.All.Where(view => WorkQueueRules.BookingIn(view).Compile()(booking)).ToList();

        Assert.Equal([expectedView], matches);
        Assert.Equal(expectedLabel, WorkQueueRules.BookingLabel(booking));
    }

    [Theory]
    [InlineData(PartnerCleaningRequest.StatusRequested, false, WorkQueueViews.NeedsAction, "Unassigned")]
    [InlineData(PartnerCleaningRequest.StatusOffered, true, WorkQueueViews.NeedsAction, "Awaiting acceptance")]
    [InlineData(PartnerCleaningRequest.StatusCancelled, false, WorkQueueViews.Cancelled, "Cancelled")]
    public void Partner_request_without_a_visit_classifies_into_one_view(string status, bool offered, string expectedView, string expectedLabel)
    {
        var request = new PartnerCleaningRequest { Status = status, OfferedEmployeeId = offered ? Guid.NewGuid() : null };

        Assert.Equal(expectedView, WorkQueueRules.PartnerView(request, []));
        Assert.Equal(expectedLabel, WorkQueueRules.PartnerLabel(request, expectedView));
    }

    [Fact]
    public void Accepted_visit_moves_from_scheduled_to_in_progress_to_completed_as_its_forms_progress()
    {
        var request = new PartnerCleaningRequest { Status = PartnerCleaningRequest.StatusAccepted };
        var notStarted = new QualityCycleForm { IsSubmitted = false };
        var started = new QualityCycleForm { IsSubmitted = false, StartedAtUtc = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc) };
        var submitted = new QualityCycleForm { IsSubmitted = true, StartedAtUtc = started.StartedAtUtc };

        Assert.Equal(WorkQueueViews.Scheduled, WorkQueueRules.PartnerView(request, []));
        Assert.Equal(WorkQueueViews.Scheduled, WorkQueueRules.PartnerView(request, [notStarted]));
        Assert.Equal(WorkQueueViews.InProgress, WorkQueueRules.PartnerView(request, [started]));
        Assert.Equal(WorkQueueViews.InProgress, WorkQueueRules.PartnerView(request, [submitted, notStarted]));
        Assert.Equal(WorkQueueViews.Completed, WorkQueueRules.PartnerView(request, [submitted]));
        Assert.Equal(WorkQueueViews.Completed, WorkQueueRules.PartnerView(request, [submitted, new QualityCycleForm { IsSubmitted = true, ShiftDeletedUtc = DateTime.UtcNow }]));
        Assert.Equal("Completed", WorkQueueRules.PartnerLabel(request, WorkQueueViews.Completed));
    }

    [Fact]
    public void A_cancelled_visit_is_never_completed_even_when_its_forms_were_submitted()
    {
        var request = new PartnerCleaningRequest { Status = PartnerCleaningRequest.StatusCancelled };

        Assert.Equal(WorkQueueViews.Cancelled, WorkQueueRules.PartnerView(request, [new QualityCycleForm { IsSubmitted = true }]));
    }

    [Fact]
    public void Bookings_in_progress_is_empty_because_bookings_have_no_stored_in_progress_state()
    {
        var booking = new Booking { Status = Booking.StatusConfirmed, EmployeeId = "employee-1" };

        Assert.False(WorkQueueRules.BookingIn(WorkQueueViews.InProgress).Compile()(booking));
    }

    [Theory]
    [InlineData("needs-action", true)]
    [InlineData("in-progress", true)]
    [InlineData("completed", true)]
    [InlineData("cancelled", true)]
    [InlineData("scheduled", true)]
    [InlineData("all", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Views_AcceptOnlyKnownValues(string? view, bool expected) =>
        Assert.Equal(expected, WorkQueueViews.IsKnown(view));

    [Fact]
    public void Every_stored_partner_status_appears_in_exactly_one_view()
    {
        string[] stored = [PartnerCleaningRequest.StatusRequested, PartnerCleaningRequest.StatusOffered,
            PartnerCleaningRequest.StatusAccepted, PartnerCleaningRequest.StatusCancelled];
        foreach (var status in stored)
        {
            var request = new PartnerCleaningRequest { Status = status };
            Assert.NotNull(WorkQueueRules.PartnerView(request, []));
        }
    }

    [Fact]
    public void Cancelled_occurrence_is_cancelled_even_when_its_form_was_submitted()
    {
        var visit = Visit(start: new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc), cancelled: true, employees: [Guid.NewGuid()]);

        Assert.Equal(WorkQueueViews.Cancelled, WorkQueueRules.ShiftView(visit, [SubmittedForm(visit.EmployeeIds[0])], DateTime.UtcNow));
    }

    [Fact]
    public void Future_occurrence_without_a_cleaner_needs_action_and_with_one_is_scheduled()
    {
        var now = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
        var unassigned = Visit(start: now.AddDays(2), employees: []);
        var assigned = Visit(start: now.AddDays(2), employees: [Guid.NewGuid()]);

        Assert.Equal(WorkQueueViews.NeedsAction, WorkQueueRules.ShiftView(unassigned, [], now));
        Assert.Equal("Unassigned", WorkQueueRules.ShiftLabel(unassigned, WorkQueueViews.NeedsAction, now));
        Assert.Equal(WorkQueueViews.Scheduled, WorkQueueRules.ShiftView(assigned, [], now));
        Assert.Equal("Scheduled", WorkQueueRules.ShiftLabel(assigned, WorkQueueViews.Scheduled, now));
    }

    [Fact]
    public void Completed_needs_a_submitted_form_from_every_assigned_cleaner()
    {
        var now = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var visit = Visit(start: now.AddDays(-3), employees: [first, second]);

        Assert.Equal(WorkQueueViews.InProgress, WorkQueueRules.ShiftView(visit, [SubmittedForm(first), StartedForm(second)], now));
        Assert.Equal(WorkQueueViews.InProgress, WorkQueueRules.ShiftView(visit, [SubmittedForm(first)], now));
        Assert.Equal(WorkQueueViews.Completed, WorkQueueRules.ShiftView(visit, [SubmittedForm(first), SubmittedForm(second)], now));
        Assert.Equal("Completed", WorkQueueRules.ShiftLabel(visit, WorkQueueViews.Completed, now));
    }

    [Fact]
    public void Past_assigned_visit_nobody_started_is_labelled_not_started_while_future_assigned_stays_scheduled()
    {
        var now = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
        var pastAssigned = Visit(start: now.AddDays(-1), employees: [Guid.NewGuid()]);
        var futureAssigned = Visit(start: now.AddDays(1), employees: [Guid.NewGuid()]);
        var pastUnassigned = Visit(start: now.AddDays(-1), employees: []);
        var futureUnassigned = Visit(start: now.AddDays(1), employees: []);

        Assert.Equal(WorkQueueViews.Scheduled, WorkQueueRules.ShiftView(pastAssigned, [], now));
        Assert.Equal("Not started", WorkQueueRules.ShiftLabel(pastAssigned, WorkQueueViews.Scheduled, now));
        Assert.Equal("Scheduled", WorkQueueRules.ShiftLabel(futureAssigned, WorkQueueViews.Scheduled, now));
        Assert.Equal("Not started", WorkQueueRules.ShiftLabel(pastUnassigned, WorkQueueViews.Scheduled, now));
        Assert.Equal("Unassigned", WorkQueueRules.ShiftLabel(futureUnassigned, WorkQueueViews.NeedsAction, now));
    }

    [Fact]
    public void Visit_starting_exactly_now_counts_as_upcoming_not_missed()
    {
        var now = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
        var atNow = Visit(start: now, employees: [Guid.NewGuid()]);
        Assert.Equal("Scheduled", WorkQueueRules.ShiftLabel(atNow, WorkQueueViews.Scheduled, now));
    }

    [Fact]
    public void Past_occurrence_nobody_started_is_shown_as_not_started_not_hidden()
    {
        var now = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
        var missed = Visit(start: now.AddDays(-1), employees: []);

        Assert.Equal(WorkQueueViews.Scheduled, WorkQueueRules.ShiftView(missed, [], now));
        Assert.Equal("Not started", WorkQueueRules.ShiftLabel(missed, WorkQueueViews.Scheduled, now));
    }

    private static ShiftVisitRosterEntry Visit(DateTime start, IReadOnlyList<Guid> employees, bool cancelled = false) =>
        new(new ResolvedShiftOccurrence(start, start.AddHours(2), Status: cancelled ? ShiftOccurrenceExceptionStatus.CustomerCancelled : null),
            null, null, employees, "Schedule", null);

    private static QualityCycleForm SubmittedForm(Guid employee) => new() { EmployeeId = employee, IsSubmitted = true, StartedAtUtc = DateTime.UtcNow };

    private static QualityCycleForm StartedForm(Guid employee) => new() { EmployeeId = employee, IsSubmitted = false, StartedAtUtc = DateTime.UtcNow };
}
