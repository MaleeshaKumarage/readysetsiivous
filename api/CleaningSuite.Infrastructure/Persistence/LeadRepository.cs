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
            // Avoid StringComparison overloads — Marten cannot translate them to SQL.
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(l =>
                l.Name.ToLower().Contains(term)
                || (l.BusinessId != null && l.BusinessId.ToLower().Contains(term))
                || (l.MainContactName != null && l.MainContactName.ToLower().Contains(term))
                || (l.MainContactEmail != null && l.MainContactEmail.ToLower().Contains(term)));
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
