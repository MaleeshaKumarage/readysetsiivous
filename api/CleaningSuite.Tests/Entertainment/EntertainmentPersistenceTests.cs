using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CleaningSuite.Tests.Entertainment;

public class EntertainmentPersistenceTests
{
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "game_" + Guid.NewGuid().ToString("N"));
        return services.BuildServiceProvider();
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Duplicate_reservation_rolls_back_round_and_group_preserving_legacy_history()
    {
        using var services = Services();
        var store = services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var repository = new EntertainmentRepository(store);
        var group = EntertainmentGroup.Create("Game fixture", "preserved-legacy-key", "Test fixture topic", "G" + Guid.NewGuid().ToString("N"));
        var player = EntertainmentPlayer.Create(group.Id, "Fixture host", CefrLevel.A1, true);
        await repository.CreateGroupAsync(group, player);
        try
        {
            var legacy = EntertainmentRound.Create(group.Id, 0, player.Id, "Legacy history sentence.", "Vanha lause.", []);
            await repository.SaveRoundAsync(legacy);
            var first = EntertainmentRound.Create(group.Id, 1, player.Id, "The cat sits.", "Kissa istuu.", []);
            group.CurrentRoundId = first.Id;
            group.Status = EntertainmentStatus.InGame;
            await repository.SaveGeneratedRoundAsync(group, first);
            var reload = (await repository.GetGroupAsync(group.Id))!;
            var duplicate = EntertainmentRound.Create(group.Id, 2, player.Id, " THE CAT sits! ", "Kissa istuu.", []);
            reload.CurrentRoundId = duplicate.Id;
            await Assert.ThrowsAsync<EntertainmentDuplicateSentenceException>(() => repository.SaveGeneratedRoundAsync(reload, duplicate));
            Assert.Null(await repository.GetRoundAsync(duplicate.Id));
            var durable = (await repository.GetGroupAsync(group.Id))!;
            Assert.Equal(first.Id, durable.CurrentRoundId);
            Assert.Equal("preserved-legacy-key", durable.GeminiApiKeyEncrypted);
            Assert.Contains(legacy.SentenceEn, await repository.ListAllSentencesAsync(group.Id));
            Assert.Equal(first.Id, (await repository.GetLatestRoundAsync(group.Id))!.Id);
        }
        finally { await repository.DeleteGroupAsync(group.Id); }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Parallel_reservations_accept_exactly_one_round_and_state_survives_reload()
    {
        using var services = Services();
        var store = services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var repository = new EntertainmentRepository(store);
        var group = EntertainmentGroup.Create("Game fixture", "", "Test fixture topic", "G" + Guid.NewGuid().ToString("N"));
        var player = EntertainmentPlayer.Create(group.Id, "Fixture host", CefrLevel.A1, true);
        await repository.CreateGroupAsync(group, player);
        try
        {
            var groups = new[] { (await repository.GetGroupAsync(group.Id))!, (await repository.GetGroupAsync(group.Id))! };
            var rounds = Enumerable.Range(1, 2).Select(n => EntertainmentRound.Create(group.Id, n, player.Id, "I visit Helsinki tomorrow.", "Käyn huomenna Helsingissä.", [])).ToArray();
            async Task<bool> Save(int i)
            {
                groups[i].CurrentRoundId = rounds[i].Id;
                groups[i].Status = EntertainmentStatus.InGame;
                try { await repository.SaveGeneratedRoundAsync(groups[i], rounds[i]); return true; }
                catch (EntertainmentDuplicateSentenceException) { return false; }
            }
            var results = await Task.WhenAll(Save(0), Save(1));
            Assert.Single(results.Where(x => x));
            var durable = (await repository.GetGroupAsync(group.Id))!;
            var round = (await repository.GetRoundAsync(durable.CurrentRoundId!.Value))!;
            durable.TurnOrder = [player.Id];
            durable.Scores[player.Id] = 5;
            round.Hints = [new("visit", "käydä")];
            round.RevealedHintIndexes = [0];
            round.SubmittedAnswer = "Käyn huomenna Helsingissä.";
            round.Evaluation = new(true, round.SubmittedAnswer, "Correct translation.", [], 7, 3, 10);
            await repository.SaveGameStateAsync(durable, round);
            var restored = (await repository.GetRoundAsync(round.Id))!;
            Assert.True(restored.IsEvaluated);
            Assert.Equal(round.SubmittedAnswer, restored.SubmittedAnswer);
            Assert.Equal("käydä", restored.Hints[0].Finnish);
            Assert.Equal(5, (await repository.GetGroupAsync(group.Id))!.Scores[player.Id]);
        }
        finally { await repository.DeleteGroupAsync(group.Id); }
    }
}
