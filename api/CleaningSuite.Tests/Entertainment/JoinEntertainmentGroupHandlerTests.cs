using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class JoinEntertainmentGroupHandlerTests
{
    private static EntertainmentGroup NewGroup(EntertainmentStatus status = EntertainmentStatus.Lobby)
    {
        var group = EntertainmentGroup.Create("Travel English", "enc", new string('x', 200), "ABC234");
        group.Status = status;
        return group;
    }

    [Fact]
    public async Task Handle_ValidJoin_CreatesPlayerAndReturnsResult()
    {
        var group = NewGroup();
        var repo = new Mock<IEntertainmentRepository>();
        repo.Setup(r => r.GetGroupByJoinKeyAsync("ABC234", It.IsAny<CancellationToken>())).ReturnsAsync(group);

        EntertainmentPlayer? saved = null;
        repo.Setup(r => r.SavePlayerAsync(It.IsAny<EntertainmentPlayer>(), It.IsAny<CancellationToken>()))
            .Callback<EntertainmentPlayer, CancellationToken>((p, _) => saved = p)
            .Returns(Task.CompletedTask);

        var handler = new JoinEntertainmentGroupCommandHandler(repo.Object);

        var result = await handler.Handle(
            new JoinEntertainmentGroupCommand("ABC234", "Bob", "B2"), CancellationToken.None);

        Assert.Equal(group.Id, result.GroupId);
        Assert.Equal("Bob", result.PlayerName);
        Assert.Equal("B2", result.Level);
        Assert.NotNull(saved);
        Assert.False(saved!.IsCreator);
        Assert.Equal(group.Id, saved.GroupId);
        Assert.Equal(CefrLevel.B2, saved.Level);
    }

    [Fact]
    public async Task Handle_UnknownJoinKey_ThrowsNotFound()
    {
        var repo = new Mock<IEntertainmentRepository>();
        repo.Setup(r => r.GetGroupByJoinKeyAsync("ZZZZZZ", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntertainmentGroup?)null);

        var handler = new JoinEntertainmentGroupCommandHandler(repo.Object);

        await Assert.ThrowsAsync<EntertainmentGroupNotFoundException>(() =>
            handler.Handle(new JoinEntertainmentGroupCommand("ZZZZZZ", "Bob", "A1"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_GroupAlreadyStarted_ThrowsClosed()
    {
        var group = NewGroup(EntertainmentStatus.InGame);
        var repo = new Mock<IEntertainmentRepository>();
        repo.Setup(r => r.GetGroupByJoinKeyAsync("ABC234", It.IsAny<CancellationToken>())).ReturnsAsync(group);

        var handler = new JoinEntertainmentGroupCommandHandler(repo.Object);

        await Assert.ThrowsAsync<EntertainmentJoinClosedException>(() =>
            handler.Handle(new JoinEntertainmentGroupCommand("ABC234", "Bob", "A1"), CancellationToken.None));
    }
}
