using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
namespace CleaningSuite.Api.Hubs;

/// <summary>Connection/presence and bounded per-game locks only; game state is durable.</summary>
public sealed class GameSessionManager
{
    private sealed class Room { public SemaphoreSlim Gate { get; } = new(1,1); public Dictionary<Guid,string> Connections { get; } = new(); }
    private readonly ConcurrentDictionary<Guid,Room> _rooms = new();
    private readonly ConcurrentDictionary<string,(Guid Group,Guid Player)> _connections = new();
    private readonly ConcurrentDictionary<string,Queue<DateTime>> _actions = new();
    public async Task<IAsyncDisposable> AcquireAsync(Guid groupId,CancellationToken ct=default)
    {
        if (!_rooms.ContainsKey(groupId) && _rooms.Count >= 1000) throw new HubException("Game capacity reached. Please retry later.");
        var room=_rooms.GetOrAdd(groupId,_=>new());
        if (!await room.Gate.WaitAsync(TimeSpan.FromSeconds(1),ct)) throw new HubException("An action is already being processed. Please wait and retry.");
        return new Lease(room.Gate);
    }
    private sealed class Lease(SemaphoreSlim gate):IAsyncDisposable { public ValueTask DisposeAsync(){gate.Release();return ValueTask.CompletedTask;} }
    public string? GetConnectionId(Guid groupId,Guid playerId) {
        if (!_rooms.TryGetValue(groupId,out var room)) return null;
        lock(room.Connections) return room.Connections.GetValueOrDefault(playerId);
    }
    public bool IsConnected(Guid groupId,Guid playerId)=>GetConnectionId(groupId,playerId) is not null;
    public (Guid Group,Guid Player)? GetMembership(string connectionId)=>_connections.TryGetValue(connectionId,out var value)?value:null;
    public void RegisterConnection(Guid groupId,Guid playerId,string connectionId) {
        RemoveConnection(connectionId);
        var room=_rooms.GetOrAdd(groupId,_=>new());
        lock(room.Connections) {
            if(room.Connections.TryGetValue(playerId,out var old)) { _connections.TryRemove(old,out _); _actions.TryRemove(old,out _); }
            room.Connections[playerId]=connectionId;
        }
        _connections[connectionId]=(groupId,playerId);
    }
    public void RemoveConnection(string connectionId) {
        _actions.TryRemove(connectionId,out _);
        if(!_connections.TryRemove(connectionId,out var value)||!_rooms.TryGetValue(value.Group,out var room))return;
        lock(room.Connections) if(room.Connections.GetValueOrDefault(value.Player)==connectionId) room.Connections.Remove(value.Player);
    }
    public void CheckRate(string connectionId) {
        var queue=_actions.GetOrAdd(connectionId,_=>new()); var now=DateTime.UtcNow;
        lock(queue){while(queue.Count>0 && now-queue.Peek()>TimeSpan.FromMinutes(1))queue.Dequeue();if(queue.Count>=60)throw new HubException("Too many game actions. Wait a moment and retry.");queue.Enqueue(now);}
    }
}
