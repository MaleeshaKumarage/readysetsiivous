using CleaningSuite.Application.Companies;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class CompanyRepository : ICompanyRepository
{
    private readonly ITenantSession _tenantSession;

    public CompanyRepository(ITenantSession tenantSession)
    {
        _tenantSession = tenantSession;
    }

    private IDocumentSession Session => _tenantSession.Session;

    public async Task<Company?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await Session.LoadAsync<Company>(id, ct);
    }

    public async Task<(IReadOnlyList<Company> Items, int Total)> ListAsync(
        string? search,
        int skip,
        int take,
        CancellationToken ct)
    {
        var all = await Session.Query<Company>().ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            all = all
                .Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || c.BusinessId.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var total = all.Count;
        var items = all
            .OrderBy(c => c.Name)
            .Skip(skip)
            .Take(take)
            .ToList();
        return (items, total);
    }

    public async Task SaveAsync(Company company, CancellationToken ct = default)
    {
        Session.Store(company);
        await Session.SaveChangesAsync(ct);
    }
}
