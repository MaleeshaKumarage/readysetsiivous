using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class LeadRepository : ILeadRepository
{
    private readonly ITenantSession _tenantSession;

    public LeadRepository(ITenantSession tenantSession)
    {
        _tenantSession = tenantSession;
    }

    private IDocumentSession Session => _tenantSession.Session;

    public async Task<Lead?> GetAsync(Guid id, CancellationToken ct = default) =>
        await Session.LoadAsync<Lead>(id, ct);

    public async Task<(IReadOnlyList<Lead> Items, int Total)> ListAsync(
        string? search,
        int skip,
        int take,
        CancellationToken ct)
    {
        IQueryable<Lead> query = Session.Query<Lead>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(l =>
                l.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (l.BusinessId != null && l.BusinessId.Contains(search, StringComparison.OrdinalIgnoreCase))
                || (l.MainContactName != null && l.MainContactName.Contains(search, StringComparison.OrdinalIgnoreCase))
                || (l.MainContactEmail != null && l.MainContactEmail.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(l => l.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task SaveAsync(Lead lead, CancellationToken ct = default)
    {
        Session.Store(lead);
        await Session.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Session.Delete<Lead>(id);
        await Session.SaveChangesAsync(ct);
    }
}
