using System.Security.Cryptography;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

/// <summary>
/// Drives a full game against the REAL Gemini API: create → join two players →
/// start → rotate two more rounds → end. One live Gemini call per round. Skips
/// silently when GEMINI_KEY is not set.
///   GEMINI_KEY=<key> dotnet test --filter "FullyQualifiedName~EntertainmentFullRoundLive"
/// </summary>
public class EntertainmentFullRoundLiveTests
{
    [Fact]
    public async Task FullGame_GeneratesSentencePerRound_FromLiveApi()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return; // skipped — GEMINI_KEY not set

        var protector = new EntertainmentKeyProtector(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entertainment:KeyEncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            })
            .Build());

        var gemini = new GeminiSentenceService(new HttpClient(), new ConfigurationBuilder().Build());
        var repo = new InMemoryEntertainmentRepository();

        // Create group (per-group key = GEMINI_KEY).
        var createHandler = new CreateEntertainmentGroupCommandHandler(repo, protector);
        var created = await createHandler.Handle(new CreateEntertainmentGroupCommand(
            "Travel English", apiKey, new string('x', 200), "Alice", "B1"), CancellationToken.None);

        // Two more players at different levels.
        var joinHandler = new JoinEntertainmentGroupCommandHandler(repo);
        var bob = await joinHandler.Handle(new JoinEntertainmentGroupCommand(created.JoinKey, "Bob", "A2"), CancellationToken.None);
        var carol = await joinHandler.Handle(new JoinEntertainmentGroupCommand(created.JoinKey, "Carol", "C1"), CancellationToken.None);

        // Start: round 1, translator = Alice (B1) — live Gemini call.
        var startHandler = new StartEntertainmentGameCommandHandler(repo, protector, gemini);
        var round1 = await startHandler.Handle(
            new StartEntertainmentGameCommand(created.GroupId, created.CreatorPlayerId), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(round1.Round.SentenceEn));
        Assert.False(string.IsNullOrWhiteSpace(round1.Round.SentenceFi));
        Assert.NotEmpty(round1.Round.BaseForms);

        // Real games space rounds by player time; add a delay to avoid bursting
        // the API and tripping the model's demand throttle.
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Rotate: round 2, translator = Bob (A2) — live Gemini call.
        var nextHandler = new StartNextEntertainmentRoundCommandHandler(repo, protector, gemini);
        var round2 = await nextHandler.Handle(
            new StartNextEntertainmentRoundCommand(created.GroupId, bob.PlayerId, 2), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(round2.SentenceEn));
        Assert.NotEqual(round1.Round.SentenceEn, round2.SentenceEn);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Rotate: round 3, translator = Carol (C1) — live Gemini call.
        var round3 = await nextHandler.Handle(
            new StartNextEntertainmentRoundCommand(created.GroupId, carol.PlayerId, 3), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(round3.SentenceEn));

        // End: standings for all three players.
        var endHandler = new EndEntertainmentGameCommandHandler(repo);
        var standings = await endHandler.Handle(new EndEntertainmentGameCommand(
            created.GroupId,
            created.CreatorPlayerId,
            new Dictionary<Guid, int>
            {
                [created.CreatorPlayerId] = 5,
                [bob.PlayerId] = 2,
                [carol.PlayerId] = 3,
            }), CancellationToken.None);

        Assert.Equal(3, standings.Count);
    }

    private sealed class InMemoryEntertainmentRepository : IEntertainmentRepository
    {
        private readonly Dictionary<Guid, EntertainmentGroup> _groups = new();
        private readonly Dictionary<Guid, EntertainmentPlayer> _players = new();
        private readonly Dictionary<Guid, EntertainmentRound> _rounds = new();

        public Task CreateGroupAsync(EntertainmentGroup group, EntertainmentPlayer creator, CancellationToken ct = default)
        {
            _groups[group.Id] = group;
            _players[creator.Id] = creator;
            return Task.CompletedTask;
        }

        public Task SavePlayerAsync(EntertainmentPlayer player, CancellationToken ct = default)
        {
            _players[player.Id] = player;
            return Task.CompletedTask;
        }

        public Task SaveGroupAsync(EntertainmentGroup group, CancellationToken ct = default)
        {
            _groups[group.Id] = group;
            return Task.CompletedTask;
        }

        public Task SaveRoundAsync(EntertainmentRound round, CancellationToken ct = default)
        {
            _rounds[round.Id] = round;
            return Task.CompletedTask;
        }

        public Task<EntertainmentRound?> GetRoundAsync(Guid roundId, CancellationToken ct = default)
            => Task.FromResult(_rounds.TryGetValue(roundId, out var r) ? r : null);

        public Task<bool> JoinKeyExistsAsync(string joinKey, CancellationToken ct = default)
            => Task.FromResult(_groups.Values.Any(g => g.JoinKey == joinKey));

        public Task<EntertainmentGroup?> GetGroupByJoinKeyAsync(string joinKey, CancellationToken ct = default)
            => Task.FromResult(_groups.Values.FirstOrDefault(g => g.JoinKey == joinKey));

        public Task<EntertainmentGroup?> GetGroupAsync(Guid groupId, CancellationToken ct = default)
            => Task.FromResult(_groups.TryGetValue(groupId, out var g) ? g : null);

        public Task<EntertainmentPlayer?> GetPlayerAsync(Guid playerId, CancellationToken ct = default)
            => Task.FromResult(_players.TryGetValue(playerId, out var p) ? p : null);

        public Task<IReadOnlyList<EntertainmentPlayer>> ListPlayersAsync(Guid groupId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntertainmentPlayer>>(
                _players.Values.Where(p => p.GroupId == groupId).OrderBy(p => p.JoinedUtc).ToList());
    }
}
