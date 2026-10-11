using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Marten;
using Marten.Linq;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// Entertainment persistence. Groups/players are global, not tenant-scoped, so
/// all sessions open on the fixed "entertainment" partition.
/// </summary>
public class EntertainmentRepository : IEntertainmentRepository
{
    private readonly IDocumentStore _store;

    public EntertainmentRepository(IDocumentStore store) => _store = store;

    public async Task CreateGroupAsync(
        EntertainmentGroup group,
        EntertainmentPlayer creator,
        CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(TenantIds.Entertainment);
        session.Store(group);
        session.Store(creator);
        await session.SaveChangesAsync(ct);
    }

    public async Task SavePlayerAsync(EntertainmentPlayer player, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(TenantIds.Entertainment);
        session.Store(player);
        await session.SaveChangesAsync(ct);
    }

    public async Task SaveGroupAsync(EntertainmentGroup group, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(TenantIds.Entertainment);
        session.Store(group);
        await session.SaveChangesAsync(ct);
    }

    public async Task SaveRoundAsync(EntertainmentRound round, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(TenantIds.Entertainment);
        session.Store(round);
        await session.SaveChangesAsync(ct);
    }

    public async Task<EntertainmentRound?> GetRoundAsync(Guid roundId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        return await session.LoadAsync<EntertainmentRound>(roundId, ct);
    }

    public async Task<bool> JoinKeyExistsAsync(string joinKey, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        return await session.Query<EntertainmentGroup>()
            .Where(x => x.JoinKey == joinKey)
            .AnyAsync(ct);
    }

    public async Task<EntertainmentGroup?> GetGroupByJoinKeyAsync(string joinKey, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        var results = await session.Query<EntertainmentGroup>()
            .Where(x => x.JoinKey == joinKey)
            .ToListAsync(ct);
        return results.FirstOrDefault();
    }

    public async Task<EntertainmentGroup?> GetGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        return await session.LoadAsync<EntertainmentGroup>(groupId, ct);
    }

    public async Task<EntertainmentPlayer?> GetPlayerAsync(Guid playerId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        return await session.LoadAsync<EntertainmentPlayer>(playerId, ct);
    }

    public async Task<IReadOnlyList<EntertainmentPlayer>> ListPlayersAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        return await session.Query<EntertainmentPlayer>()
            .Where(x => x.GroupId == groupId)
            .OrderBy(x => x.JoinedUtc)
            .ToListAsync(ct);
    }

    public async Task<bool> SentenceExistsAsync(Guid groupId, string sentenceEn, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        return await session.Query<EntertainmentRound>()
            .Where(x => x.GroupId == groupId && x.SentenceEn == sentenceEn)
            .AnyAsync(ct);
    }

    public async Task<IReadOnlyList<EntertainmentGroup>> ListInactiveGroupsAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Entertainment);
        return await session.Query<EntertainmentGroup>()
            .Where(x => x.UpdatedUtc < cutoffUtc)
            .ToListAsync(ct);
    }

    public async Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(TenantIds.Entertainment);
        session.Delete<EntertainmentGroup>(groupId);
        session.DeleteWhere<EntertainmentPlayer>(x => x.GroupId == groupId);
        session.DeleteWhere<EntertainmentRound>(x => x.GroupId == groupId);
        await session.SaveChangesAsync(ct);
    }
}
