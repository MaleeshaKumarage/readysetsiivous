using System.Collections.Concurrent;

namespace CleaningSuite.Api.Hubs;

/// <summary>
/// In-memory live game state. Persisted state lives in Marten; this tracks only
/// what changes per connection/round: player connections, turn order, current
/// translator, reveal counts, and running scores.
/// </summary>
public class GameSessionManager
{
    private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Guid> _connectionToGroup = new();

    public class GameSession
    {
        public Dictionary<Guid, string> ConnectionIds { get; } = new(); // playerId -> connectionId
        public List<Guid> TurnOrder { get; set; } = new();
        public int TranslatorIndex { get; set; }
        public Dictionary<Guid, int> Scores { get; } = new();
        public RoundState? CurrentRound { get; set; }
    }

    public class RoundState
    {
        public Guid RoundId { get; init; }
        public int RoundNumber { get; init; }
        public Guid TranslatorId { get; init; }
        public int TranslatorHintReveals { get; set; }
        public bool AnswerRevealed { get; set; }
    }

    public void RegisterConnection(Guid groupId, Guid playerId, string connectionId)
    {
        var session = _sessions.GetOrAdd(groupId, _ => new GameSession());
        lock (session)
        {
            session.ConnectionIds[playerId] = connectionId;
        }
        _connectionToGroup[connectionId] = groupId;
    }

    public void RemoveConnection(string connectionId)
    {
        if (!_connectionToGroup.TryRemove(connectionId, out var groupId))
            return;

        if (!_sessions.TryGetValue(groupId, out var session))
            return;

        lock (session)
        {
            var playerId = session.ConnectionIds
                .FirstOrDefault(kv => kv.Value == connectionId).Key;
            if (playerId != default)
                session.ConnectionIds.Remove(playerId);
        }
    }

    public void StartGame(Guid groupId, IReadOnlyList<Guid> turnOrder)
    {
        var session = _sessions.GetOrAdd(groupId, _ => new GameSession());
        lock (session)
        {
            session.TurnOrder = turnOrder.ToList();
            session.TranslatorIndex = 0;
            session.Scores.Clear();
            foreach (var playerId in turnOrder)
                session.Scores[playerId] = 0;
        }
    }

    public void StartRound(Guid groupId, Guid roundId, int roundNumber, Guid translatorId)
    {
        var session = _sessions.GetOrAdd(groupId, _ => new GameSession());
        lock (session)
        {
            session.CurrentRound = new RoundState
            {
                RoundId = roundId,
                RoundNumber = roundNumber,
                TranslatorId = translatorId,
            };
        }
    }

    public RoundState? GetRound(Guid groupId)
    {
        if (!_sessions.TryGetValue(groupId, out var session))
            return null;
        lock (session)
        {
            return session.CurrentRound;
        }
    }

    public void RecordTranslatorHintReveal(Guid groupId)
    {
        var session = _sessions.GetOrAdd(groupId, _ => new GameSession());
        lock (session)
        {
            if (session.CurrentRound is not null)
                session.CurrentRound.TranslatorHintReveals += 1;
        }
    }

    public void RecordAnswerRevealed(Guid groupId)
    {
        var session = _sessions.GetOrAdd(groupId, _ => new GameSession());
        lock (session)
        {
            if (session.CurrentRound is not null)
                session.CurrentRound.AnswerRevealed = true;
        }
    }

    public bool WasCleanSolve(Guid groupId)
    {
        if (!_sessions.TryGetValue(groupId, out var session))
            return false;
        lock (session)
        {
            return session.CurrentRound is not null && session.CurrentRound.TranslatorHintReveals == 0;
        }
    }

    public void ApplyDeltas(Guid groupId, IReadOnlyDictionary<Guid, int> deltas)
    {
        var session = _sessions.GetOrAdd(groupId, _ => new GameSession());
        lock (session)
        {
            foreach (var (playerId, delta) in deltas)
                session.Scores[playerId] = session.Scores.GetValueOrDefault(playerId) + delta;
        }
    }

    public IReadOnlyDictionary<Guid, int> GetScores(Guid groupId)
    {
        if (!_sessions.TryGetValue(groupId, out var session))
            return new Dictionary<Guid, int>();
        lock (session)
        {
            return new Dictionary<Guid, int>(session.Scores);
        }
    }

    public Guid? AdvanceTranslator(Guid groupId)
    {
        if (!_sessions.TryGetValue(groupId, out var session))
            return null;
        lock (session)
        {
            if (session.TurnOrder.Count == 0)
                return null;
            session.TranslatorIndex = (session.TranslatorIndex + 1) % session.TurnOrder.Count;
            return session.TurnOrder[session.TranslatorIndex];
        }
    }

    public IReadOnlyList<Guid> GetTurnOrder(Guid groupId)
    {
        if (!_sessions.TryGetValue(groupId, out var session))
            return Array.Empty<Guid>();
        lock (session)
        {
            return session.TurnOrder.ToList();
        }
    }

    public string? GetConnectionId(Guid groupId, Guid playerId)
    {
        if (!_sessions.TryGetValue(groupId, out var session))
            return null;

        lock (session)
        {
            return session.ConnectionIds.TryGetValue(playerId, out var connectionId)
                ? connectionId
                : null;
        }
    }
}
