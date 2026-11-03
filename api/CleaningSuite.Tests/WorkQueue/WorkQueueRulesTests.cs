using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Partners;

namespace CleaningSuite.Tests.WorkQueue;

public class WorkQueueRulesTests
{
    [Theory]
    [InlineData(Booking.StatusNew, false, WorkQueueViews.NeedsAction, "New booking")]
    [InlineData(Booking.StatusConfirmed, false, WorkQueueViews.NeedsAction, "Unassigned")]
    [InlineData(Booking.StatusConfirmed, true, WorkQueueViews.Scheduled, "Confirmed")]
    [InlineData(Booking.StatusCompleted, true, WorkQueueViews.Completed, "Completed")]
    [InlineData(Booking.StatusCancelled, false, WorkQueueViews.Completed, "Cancelled")]
    public void Booking_ClassifiesIntoExactlyOneView(string status, bool assigned, string expectedView, string expectedLabel)
    {
        var booking = new Booking { Status = status, EmployeeId = assigned ? "employee-1" : null };

        var matches = WorkQueueViews.All.Where(view => WorkQueueRules.BookingIn(view).Compile()(booking)).ToList();

        Assert.Equal([expectedView], matches);
        Assert.Equal(expectedLabel, WorkQueueRules.BookingLabel(booking));
    }

    [Theory]
    [InlineData("Requested", false, WorkQueueViews.NeedsAction, "Unassigned")]
    [InlineData("Requested", true, WorkQueueViews.NeedsAction, "Awaiting acceptance")]
    [InlineData("Accepted", true, WorkQueueViews.Scheduled, "Accepted")]
    [InlineData("Cancelled", false, WorkQueueViews.Completed, "Cancelled")]
    public void Partner_ClassifiesIntoExactlyOneView(string status, bool offered, string expectedView, string expectedLabel)
    {
        var request = new PartnerCleaningRequest { Status = status, OfferedEmployeeId = offered ? Guid.NewGuid() : null };

        var matches = WorkQueueViews.All.Where(view => WorkQueueRules.PartnerIn(view).Compile()(request)).ToList();

        Assert.Equal([expectedView], matches);
        Assert.Equal(expectedLabel, WorkQueueRules.PartnerLabel(request));
    }

    [Fact]
    public void InProgress_IsEmptyUntilStoredStateExists()
    {
        var booking = new Booking { Status = Booking.StatusConfirmed, EmployeeId = "employee-1" };
        var request = new PartnerCleaningRequest { Status = "Accepted" };

        Assert.False(WorkQueueRules.BookingIn(WorkQueueViews.InProgress).Compile()(booking));
        Assert.False(WorkQueueRules.PartnerIn(WorkQueueViews.InProgress).Compile()(request));
    }

    [Theory]
    [InlineData("needs-action", true)]
    [InlineData("in-progress", true)]
    [InlineData("completed", true)]
    [InlineData("scheduled", true)]
    [InlineData("all", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Views_AcceptOnlyKnownValues(string? view, bool expected) =>
        Assert.Equal(expected, WorkQueueViews.IsKnown(view));
}
