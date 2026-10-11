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
}
