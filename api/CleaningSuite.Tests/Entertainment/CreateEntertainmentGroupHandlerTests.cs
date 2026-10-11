using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class CreateEntertainmentGroupHandlerTests
{
    [Fact]
    public async Task Handle_EncryptsKey_CreatesGroupAndCreator()
    {
        var repo = new Mock<IEntertainmentRepository>();
        repo.Setup(r => r.JoinKeyExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var protector = new Mock<IEntertainmentKeyProtector>();
        protector.Setup(p => p.Protect("sk-test")).Returns("encrypted");

        EntertainmentGroup? savedGroup = null;
        EntertainmentPlayer? savedCreator = null;
        repo.Setup(r => r.CreateGroupAsync(It.IsAny<EntertainmentGroup>(), It.IsAny<EntertainmentPlayer>(), It.IsAny<CancellationToken>()))
            .Callback<EntertainmentGroup, EntertainmentPlayer, CancellationToken>((g, p, _) =>
            {
                savedGroup = g;
                savedCreator = p;
            })
            .Returns(Task.CompletedTask);

        var handler = new CreateEntertainmentGroupCommandHandler(repo.Object, protector.Object);

        var result = await handler.Handle(new CreateEntertainmentGroupCommand(
            "Travel English", "sk-test", new string('x', 200), "Alice", "B1"), CancellationToken.None);

        Assert.NotNull(savedGroup);
        Assert.NotNull(savedCreator);
        Assert.Equal("encrypted", savedGroup!.GeminiApiKeyEncrypted);
        Assert.NotEqual("sk-test", savedGroup.GeminiApiKeyEncrypted);
        Assert.Equal(6, savedGroup.JoinKey.Length);
        Assert.Equal(EntertainmentStatus.Lobby, savedGroup.Status);
        Assert.True(savedCreator!.IsCreator);
        Assert.Equal(savedGroup.Id, savedCreator.GroupId);
        Assert.Equal(result.JoinKey, savedGroup.JoinKey);
        Assert.Equal(result.CreatorPlayerId, savedCreator.Id);
    }

    [Fact]
    public async Task Handle_RetriesJoinKeyOnCollision()
    {
        var repo = new Mock<IEntertainmentRepository>();
        var keys = new Queue<bool>(new[] { true, false });
        repo.Setup(r => r.JoinKeyExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => keys.Dequeue());

        var protector = new Mock<IEntertainmentKeyProtector>();
        protector.Setup(p => p.Protect(It.IsAny<string>())).Returns("encrypted");

        var handler = new CreateEntertainmentGroupCommandHandler(repo.Object, protector.Object);

        var result = await handler.Handle(new CreateEntertainmentGroupCommand(
            "Travel English", "sk-test", new string('x', 200), "Alice", "A1"), CancellationToken.None);

        Assert.Equal(6, result.JoinKey.Length);
        repo.Verify(r => r.JoinKeyExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
