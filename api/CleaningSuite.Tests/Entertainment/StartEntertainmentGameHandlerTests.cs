using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class StartEntertainmentGameHandlerTests
{
    private static EntertainmentGroup NewGroup(EntertainmentStatus status = EntertainmentStatus.Lobby)
    {
        var group = EntertainmentGroup.Create("Travel English", "enc", new string('x', 200), "ABC234");
        group.Status = status;
        return group;
    }

    private static EntertainmentPlayer NewPlayer(Guid groupId, bool isCreator, CefrLevel level = CefrLevel.B1)
    {
        var p = EntertainmentPlayer.Create(groupId, isCreator ? "Alice" : "Bob", level, isCreator);
        return p;
    }

    private static (Mock<IEntertainmentRepository>, Mock<IEntertainmentKeyProtector>, Mock<IGeminiSentenceService>, StartEntertainmentGameCommandHandler) Setup()
    {
        var repo = new Mock<IEntertainmentRepository>();
        repo.Setup(r => r.SentenceExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.ListRecentSentencesAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        var protector = new Mock<IEntertainmentKeyProtector>();
        var gemini = new Mock<IGeminiSentenceService>();
        var handler = new StartEntertainmentGameCommandHandler(repo.Object, protector.Object, gemini.Object);
        return (repo, protector, gemini, handler);
    }

    [Fact]
    public async Task Handle_HappyPath_GeneratesRoundAndStartsGame()
    {
        var (repo, protector, gemini, handler) = Setup();
        var group = NewGroup();
        var creator = NewPlayer(group.Id, isCreator: true);
        var joiner = NewPlayer(group.Id, isCreator: false, CefrLevel.A2);

        repo.Setup(r => r.GetGroupAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        repo.Setup(r => r.GetPlayerAsync(creator.Id, It.IsAny<CancellationToken>())).ReturnsAsync(creator);
        repo.Setup(r => r.ListPlayersAsync(group.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntertainmentPlayer> { creator, joiner });
        protector.Setup(p => p.Unprotect("enc")).Returns("sk-real");
        gemini.Setup(g => g.GenerateAsync(group.Description, CefrLevel.B1, "sk-real", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiSentence("The cat sits.", "Kissa istuu.", new[] { "cat", "sit" }));

        var result = await handler.Handle(new StartEntertainmentGameCommand(group.Id, creator.Id), CancellationToken.None);

        Assert.Equal(1, result.Round.RoundNumber);
        Assert.Equal(creator.Id, result.Round.TranslatorPlayerId);
        Assert.Equal("The cat sits.", result.Round.SentenceEn);
        Assert.Equal("Kissa istuu.", result.Round.SentenceFi);
        Assert.Equal(new[] { "cat", "sit" }, result.Round.BaseForms);
        Assert.Equal(new[] { creator.Id, joiner.Id }, result.TurnOrder);
        Assert.Equal(EntertainmentStatus.InGame, group.Status);

        repo.Verify(r => r.SaveGroupAsync(group, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveRoundAsync(It.IsAny<EntertainmentRound>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_GroupNotFound_Throws()
    {
        var (repo, _, _, handler) = Setup();
        repo.Setup(r => r.GetGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntertainmentGroup?)null);

        await Assert.ThrowsAsync<EntertainmentGroupNotFoundException>(() =>
            handler.Handle(new StartEntertainmentGameCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AlreadyStarted_Throws()
    {
        var (repo, _, _, handler) = Setup();
        var group = NewGroup(EntertainmentStatus.InGame);
        repo.Setup(r => r.GetGroupAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);

        await Assert.ThrowsAsync<EntertainmentGameAlreadyStartedException>(() =>
            handler.Handle(new StartEntertainmentGameCommand(group.Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NonCreator_ThrowsUnauthorized()
    {
        var (repo, _, _, handler) = Setup();
        var group = NewGroup();
        var joiner = NewPlayer(group.Id, isCreator: false);
        repo.Setup(r => r.GetGroupAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        repo.Setup(r => r.GetPlayerAsync(joiner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(joiner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new StartEntertainmentGameCommand(group.Id, joiner.Id), CancellationToken.None));
    }
}
