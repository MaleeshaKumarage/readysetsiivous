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
        IQueryable<Company> query = Session.Query<Company>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                     || c.BusinessId.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task SaveAsync(Company company, CancellationToken ct = default)
    {
        Session.Store(company);
        try
        {
            await Session.SaveChangesAsync(ct);
        }
        catch (Exception error) when (IsBusinessIdConflict(error))
        {
            throw new CompanyConflictException();
        }
    }

    private static bool IsBusinessIdConflict(Exception error) =>
        error is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
            ConstraintName: MartenConfig.CompanyBusinessIdIndexName
        }
        || error.InnerException is not null && IsBusinessIdConflict(error.InnerException);
}
