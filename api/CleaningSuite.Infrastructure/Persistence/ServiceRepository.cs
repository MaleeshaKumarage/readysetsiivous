using CleaningSuite.Application.Services;
using CleaningSuite.Domain.Services;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>Services live in the request tenant's partition via ITenantSession.</summary>
public class ServiceRepository : IServiceRepository
{
    private readonly ITenantSession _tenantSession;

    public ServiceRepository(ITenantSession tenantSession) => _tenantSession = tenantSession;

    public async Task<IReadOnlyList<Service>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        IQueryable<Service> query = _tenantSession.Session.Query<Service>();
        if (!includeInactive)
            query = query.Where(s => s.IsActive);

        return await query.OrderBy(s => s.SortOrder).ToListAsync(ct);
    }

    public Task<Service?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _tenantSession.Session.LoadAsync<Service>(id, ct);

    public async Task SaveAsync(Service service, CancellationToken ct = default)
    {
        _tenantSession.Session.Store(service);
        await _tenantSession.Session.SaveChangesAsync(ct);
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid? exceptId = null, CancellationToken ct = default)
    {
        var matches = await _tenantSession.Session.Query<Service>()
            .Where(s => s.Slug == slug && (exceptId == null || s.Id != exceptId))
            .ToListAsync(ct);
        return matches.Count > 0;
    }
}
