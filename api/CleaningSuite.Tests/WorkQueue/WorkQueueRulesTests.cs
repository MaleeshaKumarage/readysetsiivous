using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Tests.WorkQueue;

public class WorkQueueRulesTests
{
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
}
