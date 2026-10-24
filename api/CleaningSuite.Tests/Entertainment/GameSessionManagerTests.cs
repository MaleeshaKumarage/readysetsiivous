using CleaningSuite.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CleaningSuite.Tests.Entertainment;

public class GameSessionManagerTests
{
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
    private static GameSessionManager Create(Clock clock) => new(clock);

    [Fact]
    public async Task MoreThanCapacitySequentialRoomsTurnOverAfterGrace()
    {
        var clock = new Clock(); var sessions = Create(clock);
        for (var i = 0; i < 1100; i++)
        {
            var group = Guid.NewGuid(); var player = Guid.NewGuid(); var connection = i.ToString();
            await using (await sessions.AcquireAsync(group))
            {
                sessions.RegisterConnection(group, player, connection);
                sessions.CheckRate(connection);
                sessions.RemoveConnection(connection);
            }
            clock.Advance(TimeSpan.FromMinutes(10));
        }
        await using var final = await sessions.AcquireAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task ActiveLeasesAndConnectionsEnforceSameCapacityAndExistingRoomStillWorks()
    {
        var clock = new Clock(); var sessions = Create(clock); var leases = new List<IAsyncDisposable>();
        var connected = Guid.NewGuid(); var player = Guid.NewGuid();
        sessions.RegisterConnection(connected, player, "connected");
        for (var i = 0; i < 999; i++) leases.Add(await sessions.AcquireAsync(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<HubException>(() => sessions.AcquireAsync(Guid.NewGuid()));
        Assert.Throws<HubException>(() => sessions.RegisterConnection(Guid.NewGuid(), player, "connected"));
        Assert.Equal((connected, player), sessions.GetMembership("connected")!.Value);
        await using (await sessions.AcquireAsync(connected)) { }
        foreach (var lease in leases) await lease.DisposeAsync();
        sessions.RemoveConnection("connected");
        await Assert.ThrowsAsync<HubException>(() => sessions.AcquireAsync(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromMinutes(10));
        await using var recovered = await sessions.AcquireAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task ConcurrentMixedAdmissionNeverExceedsCapacity()
    {
        var sessions = Create(new Clock()); var leases = new System.Collections.Concurrent.ConcurrentBag<IAsyncDisposable>();
        var admitted = 0;
        await Task.WhenAll(Enumerable.Range(0, 1200).Select(i => Task.Run(async () =>
        {
            try
            {
                if (i % 2 == 0) leases.Add(await sessions.AcquireAsync(Guid.NewGuid()));
                else sessions.RegisterConnection(Guid.NewGuid(), Guid.NewGuid(), i.ToString());
                Interlocked.Increment(ref admitted);
            }
            catch (HubException) { }
        })));
        Assert.Equal(1000, admitted);
        foreach (var lease in leases) await lease.DisposeAsync();
    }

    [Fact]
    public async Task CancellationTimeoutAndDuplicateDisposeLeaveOneGateAndReclaimReferences()
    {
        var clock = new Clock(); var sessions = Create(clock); var group = Guid.NewGuid();
        var holder = await sessions.AcquireAsync(group);
        using var cancellation = new CancellationTokenSource();
        var canceled = sessions.AcquireAsync(group, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        await Assert.ThrowsAsync<HubException>(() => sessions.AcquireAsync(group));
        var queued = sessions.AcquireAsync(group);
        clock.Advance(TimeSpan.FromDays(1));
        await using (await sessions.AcquireAsync(Guid.NewGuid())) { }
        Assert.False(queued.IsCompleted);
        await holder.DisposeAsync();
        var successor = await queued;
        await holder.DisposeAsync();
        using var probeCancellation = new CancellationTokenSource();
        var probe = sessions.AcquireAsync(group, probeCancellation.Token);
        Assert.False(probe.IsCompleted); // Duplicate disposal must not release successor's gate.
        probeCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
        await successor.DisposeAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        // Fill the registry to prove canceled/timed-out references did not strand the old room.
        var leases = new List<IAsyncDisposable>();
        for (var i = 0; i < 1000; i++) leases.Add(await sessions.AcquireAsync(Guid.NewGuid()));
        foreach (var lease in leases) await lease.DisposeAsync();
    }

    [Fact]
    public async Task ReconnectAndLateDisconnectPreserveNewMembershipAndResetActionState()
    {
        var clock = new Clock(); var sessions = Create(clock); var group = Guid.NewGuid(); var player = Guid.NewGuid();
        sessions.RegisterConnection(group, player, "old");
        for (var i = 0; i < 60; i++) sessions.CheckRate("old");
        Assert.Throws<HubException>(() => sessions.CheckRate("old"));
        for (var i = 0; i < 100; i++)
        {
            var previous = i == 0 ? "old" : (i - 1).ToString(); var replacement = i.ToString();
            await Task.WhenAll(Task.Run(() => sessions.RegisterConnection(group, player, replacement)), Task.Run(() => sessions.RemoveConnection(previous)));
            Assert.Equal(replacement, sessions.GetConnectionId(group, player));
            Assert.Null(sessions.GetMembership(previous));
            Assert.Throws<HubException>(() => sessions.CheckRate(previous));
            sessions.CheckRate(replacement);
            clock.Advance(TimeSpan.FromDays(1));
            await using var lease = await sessions.AcquireAsync(group);
        }
        sessions.RemoveConnection("99");
        sessions.RegisterConnection(group, player, "old");
        sessions.CheckRate("old");
    }

    [Fact]
    public async Task ConfiguredGraceAndConcurrentDisposalAreSupported()
    {
        var clock = new Clock();
        var sessions = new GameSessionManager(clock, Options.Create(new GameSessionOptions { IdleRetention = TimeSpan.Zero }));
        var lease = await sessions.AcquireAsync(Guid.NewGuid());
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () => await lease.DisposeAsync())));
        for (var i = 0; i < 1001; i++) { await using var next = await sessions.AcquireAsync(Guid.NewGuid()); }
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSessionManager(clock, Options.Create(new GameSessionOptions { IdleRetention = TimeSpan.FromSeconds(-1) })));
    }
}
