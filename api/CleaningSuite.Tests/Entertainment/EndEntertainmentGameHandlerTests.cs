using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class EndEntertainmentGameHandlerTests
{
    [Fact]
    public async Task Handle_SortsStandings_PersistsScores_FinishesGroup()
    {
        var group = EntertainmentGroup.Create("Travel English", "enc", new string('x', 200), "ABC234");
        var creator = EntertainmentPlayer.Create(group.Id, "Alice", CefrLevel.B1, isCreator: true);
        var bob = EntertainmentPlayer.Create(group.Id, "Bob", CefrLevel.A2, isCreator: false);

        var repo = new Mock<IEntertainmentRepository>();
        repo.Setup(r => r.GetGroupAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        repo.Setup(r => r.GetPlayerAsync(creator.Id, It.IsAny<CancellationToken>())).ReturnsAsync(creator);
        repo.Setup(r => r.ListPlayersAsync(group.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntertainmentPlayer> { creator, bob });

        var handler = new EndEntertainmentGameCommandHandler(repo.Object);

        var scores = new Dictionary<Guid, int> { [creator.Id] = 2, [bob.Id] = 7 };
        var standings = await handler.Handle(
            new EndEntertainmentGameCommand(group.Id, creator.Id, scores), CancellationToken.None);

        Assert.Equal(EntertainmentStatus.Finished, group.Status);
        Assert.Equal(2, standings.Count);
        Assert.Equal("Bob", standings[0].Name);
        Assert.Equal(7, standings[0].Score);
        Assert.Equal("Alice", standings[1].Name);
        Assert.Equal(2, standings[1].Score);

        repo.Verify(r => r.SavePlayerAsync(It.IsAny<EntertainmentPlayer>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        repo.Verify(r => r.SaveGroupAsync(group, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonCreator_ThrowsUnauthorized()
    {
        var group = EntertainmentGroup.Create("Travel English", "enc", new string('x', 200), "ABC234");
        var bob = EntertainmentPlayer.Create(group.Id, "Bob", CefrLevel.A2, isCreator: false);

        var repo = new Mock<IEntertainmentRepository>();
        repo.Setup(r => r.GetGroupAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        repo.Setup(r => r.GetPlayerAsync(bob.Id, It.IsAny<CancellationToken>())).ReturnsAsync(bob);

        var handler = new EndEntertainmentGameCommandHandler(repo.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new EndEntertainmentGameCommand(group.Id, bob.Id, new Dictionary<Guid, int>()), CancellationToken.None));
    }
}
