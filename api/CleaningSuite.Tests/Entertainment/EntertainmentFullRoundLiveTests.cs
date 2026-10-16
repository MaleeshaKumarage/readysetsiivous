using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

/// <summary>Explicit bounded live provider smoke. Requires ENTERTAINMENT_LIVE_SMOKE=1
/// and the server-owned Gemini key. Ordinary CI does not call an external provider.</summary>
public class EntertainmentFullRoundLiveTests
{
    [Fact]
    public async Task BoundedSmoke_GeneratesAndEvaluatesOneRoundWithLiveGemini()
    {
        var apiKey = Environment.GetEnvironmentVariable("GAME_GEMINI_API_KEY") ?? Environment.GetEnvironmentVariable("GEMINI_KEY");
        if (Environment.GetEnvironmentVariable("ENTERTAINMENT_LIVE_SMOKE") != "1" || string.IsNullOrWhiteSpace(apiKey))
            return; // skipped — live smoke not opted in

        await RunFullGameAsync(apiKey);
    }

    private static async Task RunFullGameAsync(string apiKey, IGeminiSentenceService? sentenceService = null)
    {
        var gemini = sentenceService ?? new GeminiSentenceService(new HttpClient(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Entertainment:Gemini:ApiKey"] = apiKey }).Build());
        var repo = new InMemoryEntertainmentRepository();

        // Create group with no per-user credential.
        var createHandler = new CreateEntertainmentGroupCommandHandler(repo);
        var created = await createHandler.Handle(new CreateEntertainmentGroupCommand(
            "Travel English", new string('x', 200), "Alice", "B1"), CancellationToken.None);

        // Generate one round and check its reference answer with one bounded call.
        var startHandler = new StartEntertainmentGameCommandHandler(repo, gemini);
        var round1 = await startHandler.Handle(
            new StartEntertainmentGameCommand(created.GroupId, created.CreatorPlayerId), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(round1.Round.SentenceEn));
        Assert.False(string.IsNullOrWhiteSpace(round1.Round.SentenceFi));
        Assert.NotEmpty(round1.Round.BaseForms);

        var evaluator = new GeminiAnswerEvaluator(new HttpClient(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Entertainment:Gemini:ApiKey"] = apiKey }).Build());
        var evaluation = await evaluator.EvaluateAsync(round1.Round.SentenceEn, round1.Round.SentenceFi, round1.Round.SentenceFi, CefrLevel.B1);
        Assert.True(evaluation.IsCorrect);
        Assert.Equal(10, EntertainmentScoring.ScoreAnswer(evaluation));

        // End with points calculated by the same internal rubric.
        var endHandler = new EndEntertainmentGameCommandHandler(repo);
        var standings = await endHandler.Handle(new EndEntertainmentGameCommand(
            created.GroupId,
            created.CreatorPlayerId,
            new Dictionary<Guid, int>
            {
                [created.CreatorPlayerId] = EntertainmentScoring.ScoreAnswer(evaluation),
            }), CancellationToken.None);

        Assert.Single(standings);
    }

    private sealed class InMemoryEntertainmentRepository : IEntertainmentRepository
    {
        private readonly Dictionary<Guid, EntertainmentGroup> _groups = new();
        private readonly Dictionary<Guid, EntertainmentPlayer> _players = new();
        private readonly Dictionary<Guid, EntertainmentRound> _rounds = new();

        public Task JoinPlayerAsync(EntertainmentGroup group, EntertainmentPlayer player, CancellationToken ct = default) => SavePlayerAsync(player, ct);
        public Task SaveGeneratedRoundAsync(EntertainmentGroup group, EntertainmentRound round, CancellationToken ct = default)
        {
            if (_rounds.Values.Any(r => r.GroupId == group.Id && EntertainmentSentenceIdentity.Normalize(r.SentenceEn) == EntertainmentSentenceIdentity.Normalize(round.SentenceEn))) throw new EntertainmentDuplicateSentenceException();
            _rounds[round.Id] = round; _groups[group.Id] = group;
            return Task.CompletedTask;
        }
        public Task SaveGameStateAsync(EntertainmentGroup group, EntertainmentRound round, CancellationToken ct = default)
        { _rounds[round.Id] = round; _groups[group.Id] = group; return Task.CompletedTask; }
        public Task<IReadOnlyList<string>> ListAllSentencesAsync(Guid groupId, CancellationToken ct = default) => ListRecentSentencesAsync(groupId, int.MaxValue, ct);
        public Task<EntertainmentRound?> GetLatestRoundAsync(Guid groupId, CancellationToken ct = default) => Task.FromResult(_rounds.Values.Where(r => r.GroupId == groupId).OrderByDescending(r => r.RoundNumber).FirstOrDefault());
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

        public Task<bool> SentenceExistsAsync(Guid groupId, string sentenceEn, CancellationToken ct = default)
            => Task.FromResult(_rounds.Values.Any(r => r.GroupId == groupId && r.SentenceEn == sentenceEn));

        public Task<IReadOnlyList<string>> ListRecentSentencesAsync(Guid groupId, int count, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(
                _rounds.Values.Where(r => r.GroupId == groupId).OrderByDescending(r => r.StartedUtc).Take(count).Select(r => r.SentenceEn).ToList());

        public Task<IReadOnlyList<EntertainmentGroup>> ListInactiveGroupsAsync(DateTime cutoffUtc, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntertainmentGroup>>(
                _groups.Values.Where(g => g.UpdatedUtc < cutoffUtc).ToList());

        public Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default)
        {
            _groups.Remove(groupId);
            foreach (var p in _players.Values.Where(p => p.GroupId == groupId).ToList())
                _players.Remove(p.Id);
            foreach (var r in _rounds.Values.Where(r => r.GroupId == groupId).ToList())
                _rounds.Remove(r.Id);
            return Task.CompletedTask;
        }
    }
}
