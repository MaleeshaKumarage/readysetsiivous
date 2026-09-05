using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using Marten;
using Marten.Linq;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>Registry partition implementation. Registry is a fixed tenant id, not the request tenant.</summary>
public class TenantRegistry : ITenantRegistry
{
    private readonly IDocumentStore _store;

    public TenantRegistry(IDocumentStore store) => _store = store;

    public async Task<TenantRegistration?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Registry);
        var results = await session.Query<TenantRegistration>()
            .Where(x => x.Slug == slug).ToListAsync(ct);
        return results.FirstOrDefault();
    }

    public async Task<TenantRegistration?> GetByRealmAsync(string realm, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Registry);
        var results = await session.Query<TenantRegistration>()
            .Where(x => x.KeycloakRealm == realm).ToListAsync(ct);
        return results.FirstOrDefault();
    }

    public async Task SaveAsync(TenantRegistration registration, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(TenantIds.Registry);
        session.Store(registration);
        await session.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TenantRegistration>> ListAsync(CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(TenantIds.Registry);
        return await session.Query<TenantRegistration>().ToListAsync(ct);
    }
}
