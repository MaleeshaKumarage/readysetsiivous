using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Bookings.Commands;
using CleaningSuite.Application.Common;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Bookings;

public class PublicBookingNotFoundTests
{
    [Fact]
    public async Task Cancelling_an_unknown_booking_explains_the_next_step_without_exposing_an_id()
    {
        var repository = new Mock<IBookingRepository>();
        repository.Setup(x => x.GetByNumberAsync("NOPE-0000", It.IsAny<CancellationToken>())).ReturnsAsync((CleaningSuite.Domain.Bookings.Booking?)null);
        var handler = new CancelPublicBookingHandler(repository.Object);
        var ex = await Assert.ThrowsAsync<PublicBookingNotFoundException>(() => handler.Handle(new CancelPublicBookingCommand("NOPE-0000", "key"), default));
        Assert.Contains("confirmation email", ex.Message);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", ex.Message);
    }
}
