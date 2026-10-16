using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class EntertainmentPlatformCoreTests
{
    [Fact]
    public void Token_has_random_capability_and_constant_time_verification()
    {
        var first = EntertainmentPlayerToken.Generate();
        var second = EntertainmentPlayerToken.Generate();
        Assert.NotEqual(first, second);
        var hash = EntertainmentPlayerToken.Hash(first);
        Assert.NotEqual(first, hash);
        Assert.True(EntertainmentPlayerToken.Verify(first, hash));
        Assert.False(EntertainmentPlayerToken.Verify(second, hash));
        Assert.False(EntertainmentPlayerToken.Verify(first, "legacy-no-token"));
        Assert.False(EntertainmentPlayerToken.Verify(null, hash));
    }

    [Theory]
    [InlineData("  The CAT sits!", "the cat sits.", true)]
    [InlineData("I have 12 apples today.", "I have 14 apples today.", true)]
    [InlineData("She bought a red bag.", "She bought a blue bag.", true)]
    [InlineData("My sister visits Helsinki tomorrow.", "They walked through the park.", false)]
    public void Duplicate_policy_normalizes_and_rejects_small_template_changes(string first, string old, bool duplicate)
    {
        Assert.Equal(duplicate, EntertainmentSentenceIdentity.IsDuplicate(first, [old]));
    }

    private static (Mock<IEntertainmentRepository> Repository, Mock<IGeminiSentenceService> Provider, EntertainmentGroup Group, EntertainmentPlayer Player) Setup()
    {
        var group = EntertainmentGroup.Create("Travel", "legacy-encrypted-secret", "Travel and daily life", "ABC234");
        var player = EntertainmentPlayer.Create(group.Id, "Alice", CefrLevel.B1, true);
        var repository = new Mock<IEntertainmentRepository>();
        repository.Setup(x => x.GetGroupAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        repository.Setup(x => x.GetPlayerAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        repository.Setup(x => x.ListPlayersAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { player });
        repository.Setup(x => x.ListAllSentencesAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        return (repository, new Mock<IGeminiSentenceService>(), group, player);
    }

    [Fact]
    public async Task Full_old_history_duplicate_is_never_accepted_after_three_attempts()
    {
        var (repository, provider, group, player) = Setup();
        var history = Enumerable.Range(0, 25).Select(i => $"Historical distinct statement {i}").Append("The cat sits.").ToArray();
        repository.Setup(x => x.ListAllSentencesAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(history);
        var prompts = new List<IReadOnlyList<string>>();
        provider.Setup(x => x.GenerateAsync(group.Description, player.Level, string.Empty, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, CefrLevel, string, IReadOnlyList<string>, CancellationToken>((_, _, _, prompt, _) => prompts.Add(prompt))
            .ReturnsAsync(new GeminiSentence("The CAT sits!", "Kissa istuu.", []));
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new StartEntertainmentGameCommandHandler(repository.Object, provider.Object)
            .Handle(new(group.Id, player.Id), CancellationToken.None));
        Assert.Equal(3, prompts.Count);
        Assert.All(prompts, p => Assert.True(p.Count <= 20));
        Assert.Contains("REJECTED DUPLICATE", prompts[1][0]);
        Assert.Equal(EntertainmentStatus.Lobby, group.Status);
        Assert.Null(group.CurrentRoundId);
        repository.Verify(x => x.SaveGeneratedRoundAsync(It.IsAny<EntertainmentGroup>(), It.IsAny<EntertainmentRound>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reservation_conflict_retries_with_explicit_rejection_and_durable_new_round()
    {
        var (repository, provider, group, player) = Setup();
        provider.SetupSequence(x => x.GenerateAsync(group.Description, player.Level, string.Empty, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiSentence("The cat sits.", "Kissa istuu.", []))
            .ReturnsAsync(new GeminiSentence("Tomorrow we will visit Helsinki.", "Huomenna käymme Helsingissä.", []));
        repository.SetupSequence(x => x.SaveGeneratedRoundAsync(group, It.IsAny<EntertainmentRound>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntertainmentDuplicateSentenceException()).Returns(Task.CompletedTask);
        var result = await new StartEntertainmentGameCommandHandler(repository.Object, provider.Object).Handle(new(group.Id, player.Id), CancellationToken.None);
        Assert.Equal("Tomorrow we will visit Helsinki.", result.Round.SentenceEn);
        Assert.Equal(result.Round.RoundId, group.CurrentRoundId);
        Assert.Equal(EntertainmentStatus.InGame, group.Status);
        Assert.Equal("legacy-encrypted-secret", group.GeminiApiKeyEncrypted);
    }

    [Fact]
    public async Task Failed_durable_save_keeps_round_and_status_recoverable()
    {
        var (repository, provider, group, player) = Setup();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CefrLevel>(), string.Empty, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiSentence("The cat sits.", "Kissa istuu.", []));
        repository.Setup(x => x.SaveGeneratedRoundAsync(group, It.IsAny<EntertainmentRound>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("database unavailable"));
        await Assert.ThrowsAsync<IOException>(() => new StartEntertainmentGameCommandHandler(repository.Object, provider.Object).Handle(new(group.Id, player.Id), CancellationToken.None));
        Assert.Null(group.CurrentRoundId);
        Assert.Equal(EntertainmentStatus.Lobby, group.Status);
    }

    [Fact]
    public async Task Join_rejects_full_room_without_persisting_new_player()
    {
        var (repository, _, group, _) = Setup();
        repository.Setup(x => x.GetGroupByJoinKeyAsync(group.JoinKey, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        repository.Setup(x => x.ListPlayersAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Enumerable.Range(0, 16).Select(i => EntertainmentPlayer.Create(group.Id, $"Player {i}", CefrLevel.A1, false)).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new JoinEntertainmentGroupCommandHandler(repository.Object).Handle(new(group.JoinKey, "Bob", "A1"), CancellationToken.None));
        repository.Verify(x => x.JoinPlayerAsync(It.IsAny<EntertainmentGroup>(), It.IsAny<EntertainmentPlayer>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
