using CleaningSuite.Application.Companies;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class BranchRepository : IBranchRepository
{
    private readonly ITenantSession _tenantSession;

    public BranchRepository(ITenantSession tenantSession)
    {
        _tenantSession = tenantSession;
    }

    private IDocumentSession Session => _tenantSession.Session;

    public async Task<Branch?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await Session.LoadAsync<Branch>(id, ct);
    }

    public async Task<IReadOnlyList<Branch>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        return await Session.Query<Branch>()
            .Where(b => b.CompanyId == companyId && b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Branch>> ListByCompanyAsync(Guid companyId, bool includeInactive, CancellationToken ct = default)
    {
        var query = Session.Query<Branch>().Where(b => b.CompanyId == companyId);

        if (!includeInactive)
        {
            query = query.Where(b => b.IsActive);
        }

        return await query
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(Branch branch, CancellationToken ct = default)
    {
        Session.Store(branch);
        await Session.SaveChangesAsync(ct);
    }
}
