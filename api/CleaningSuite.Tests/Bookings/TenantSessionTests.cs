using System.Data;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Moq;

namespace CleaningSuite.Tests.Bookings;

public class TenantSessionTests
{
    [Fact]
    public async Task Reservation_discards_old_session_and_starts_fresh_serializable_session_in_same_tenant()
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns("tenant");
        var store = new Mock<IDocumentStore>();
        var oldSession = new Mock<IDocumentSession>();
        var firstAttempt = new Mock<IDocumentSession>();
        var secondAttempt = new Mock<IDocumentSession>();
        store.Setup(x => x.DirtyTrackedSession("tenant", It.IsAny<IsolationLevel>())).Returns(oldSession.Object);
        store.SetupSequence(x => x.DirtyTrackedSerializableSessionAsync("tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstAttempt.Object).ReturnsAsync(secondAttempt.Object);
        using var sessions = new TenantSessionFactory(store.Object, context.Object);

        Assert.Same(oldSession.Object, sessions.Session);
        await sessions.BeginReservationAsync();
        Assert.Same(firstAttempt.Object, sessions.Session);
        await sessions.BeginReservationAsync();
        Assert.Same(secondAttempt.Object, sessions.Session);

        oldSession.Verify(x => x.Dispose(), Times.Once);
        firstAttempt.Verify(x => x.Dispose(), Times.Once);
    }
}
