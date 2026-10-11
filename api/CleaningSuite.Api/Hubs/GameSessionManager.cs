using System.Collections.Concurrent;

namespace CleaningSuite.Api.Hubs;

/// <summary>
/// In-memory live game state. Persisted state lives in Marten; this tracks only
/// what changes per connection/round: which connection each player is on, turn
/// order, and the current translator. Used to route per-role round payloads.
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
