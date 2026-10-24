using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CleaningSuite.Api.Hubs;

public sealed class GameSessionOptions
{
    public TimeSpan IdleRetention { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Bounded, process-local presence and serialization only; game state remains durable.</summary>
public sealed class GameSessionManager
{
    private const int Capacity = 1000;
    private sealed class Room(long now)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Dictionary<Guid, string> Connections { get; } = new();
        // Includes both the holder and every pending WaitAsync, before leaving the registry lock.
        public int LeaseReferences;
        public long IdleSince = now;
    }

    private readonly object _registry = new();
    private readonly Dictionary<Guid, Room> _rooms = new();
    private readonly Dictionary<string, (Guid Group, Guid Player)> _connections = new();
    private readonly Dictionary<string, Queue<long>> _actions = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idleRetention;

    public GameSessionManager(TimeProvider? clock = null, IOptions<GameSessionOptions>? options = null)
    {
        _clock = clock ?? TimeProvider.System;
        _idleRetention = options?.Value.IdleRetention ?? TimeSpan.FromMinutes(10);
        if (_idleRetention < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }

    // Called only while holding _registry. No awaiting or I/O occurs under this lock.
    private Room Admit(Guid groupId)
    {
        var now = _clock.GetTimestamp();
        foreach (var (id, candidate) in _rooms.ToArray())
        {
            if (candidate.LeaseReferences == 0 && candidate.Connections.Count == 0 &&
                _clock.GetElapsedTime(candidate.IdleSince, now) >= _idleRetention &&
                _rooms.TryGetValue(id, out var current) && ReferenceEquals(current, candidate))
            {
                _rooms.Remove(id);
                candidate.Gate.Dispose();
            }
        }
        if (_rooms.TryGetValue(groupId, out var room)) return room;
        if (_rooms.Count >= Capacity) throw new HubException("Game capacity reached. Please retry later.");
        room = new Room(now);
        _rooms.Add(groupId, room);
        return room;
    }

    public async Task<IAsyncDisposable> AcquireAsync(Guid groupId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Room room;
        lock (_registry) { room = Admit(groupId); room.LeaseReferences++; }
        try
        {
            if (!await room.Gate.WaitAsync(TimeSpan.FromSeconds(1), ct))
                throw new HubException("An action is already being processed. Please wait and retry.");
            return new Lease(this, groupId, room);
        }
        catch { ReleaseReference(groupId, room, releaseGate: false); throw; }
    }

    private void ReleaseReference(Guid groupId, Room room, bool releaseGate)
    {
        lock (_registry)
        {
            if (releaseGate) room.Gate.Release();
            room.LeaseReferences--;
            MarkIdle(groupId, room);
        }
    }

    private void MarkIdle(Guid groupId, Room room)
    {
        if (room.LeaseReferences == 0 && room.Connections.Count == 0 &&
            _rooms.TryGetValue(groupId, out var current) && ReferenceEquals(current, room))
            room.IdleSince = _clock.GetTimestamp();
    }

    private sealed class Lease(GameSessionManager owner, Guid groupId, Room room) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.ReleaseReference(groupId, room, releaseGate: true);
            return ValueTask.CompletedTask;
        }
    }

    public string? GetConnectionId(Guid groupId, Guid playerId)
    {
        lock (_registry) return _rooms.TryGetValue(groupId, out var room) ? room.Connections.GetValueOrDefault(playerId) : null;
    }
    public bool IsConnected(Guid groupId, Guid playerId) => GetConnectionId(groupId, playerId) is not null;
    public (Guid Group, Guid Player)? GetMembership(string connectionId)
    {
        lock (_registry) return _connections.TryGetValue(connectionId, out var value) ? value : null;
    }
    public void RegisterConnection(Guid groupId, Guid playerId, string connectionId)
    {
        lock (_registry)
        {
            // Admit before removing old membership, so rejection leaves it intact.
            var room = Admit(groupId);
            RemoveConnectionCore(connectionId);
            if (room.Connections.TryGetValue(playerId, out var old)) RemoveConnectionCore(old);
            room.Connections[playerId] = connectionId;
            _connections[connectionId] = (groupId, playerId);
        }
    }
    public void RemoveConnection(string connectionId)
    {
        lock (_registry) RemoveConnectionCore(connectionId);
    }
    private void RemoveConnectionCore(string connectionId)
    {
        _actions.Remove(connectionId);
        if (!_connections.Remove(connectionId, out var membership) || !_rooms.TryGetValue(membership.Group, out var room)) return;
        if (room.Connections.GetValueOrDefault(membership.Player) == connectionId) room.Connections.Remove(membership.Player);
        MarkIdle(membership.Group, room);
    }
    public void CheckRate(string connectionId)
    {
        lock (_registry)
        {
            // A disconnected/replaced connection cannot recreate orphaned rate-limit state.
            if (!_connections.ContainsKey(connectionId)) throw new HubException("Not authorized to act as this player.");
            if (!_actions.TryGetValue(connectionId, out var queue)) _actions.Add(connectionId, queue = new());
            var now = _clock.GetTimestamp();
            while (queue.Count > 0 && _clock.GetElapsedTime(queue.Peek(), now) > TimeSpan.FromMinutes(1)) queue.Dequeue();
            if (queue.Count >= 60) throw new HubException("Too many game actions. Wait a moment and retry.");
            queue.Enqueue(now);
        }
    }
}
