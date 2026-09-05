using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Tenants;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// Explicit-tenant repository. Sessions are cached per (request, tenantId) so a
/// load and the following save share one session and its concurrency versions.
/// System requests (context tenant = registry) can still write the target
/// tenant's partition during provisioning.
/// </summary>
public class TenantProfileRepository : ITenantProfileRepository, IDisposable
{
    private readonly IDocumentStore _store;
    private readonly Dictionary<string, IDocumentSession> _sessions = new();

    public TenantProfileRepository(IDocumentStore store) => _store = store;

    public async Task<TenantProfile?> GetAsync(string tenantId, CancellationToken ct = default) =>
        await Session(tenantId).LoadAsync<TenantProfile>(
            TenantDocumentIds.TenantProfile(tenantId), ct);

    public async Task SaveAsync(string tenantId, TenantProfile profile, CancellationToken ct = default)
    {
        Session(tenantId).Store(profile);
        await Session(tenantId).SaveChangesAsync(ct);
    }

    private IDocumentSession Session(string tenantId)
    {
        if (!_sessions.TryGetValue(tenantId, out var session))
        {
            session = _store.DirtyTrackedSession(tenantId);
            _sessions[tenantId] = session;
        }

        return session;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();
    }
}
