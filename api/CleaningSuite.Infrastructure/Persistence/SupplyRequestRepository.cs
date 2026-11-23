using CleaningSuite.Application.Inventory;
using CleaningSuite.Domain.Inventory;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public sealed class SupplyRequestRepository(ITenantSession tenant) : ISupplyRequestRepository
{
    private IDocumentSession Session => tenant.Session;

    public Task<SupplyRequest?> GetAsync(Guid id, CancellationToken ct) => Session.LoadAsync<SupplyRequest>(id, ct);

    public async Task<IReadOnlyList<SupplyRequest>> ListAsync(SupplyRequestQuery query, CancellationToken ct)
    {
        IQueryable<SupplyRequest> q = Session.Query<SupplyRequest>();
        if (query.EmployeeId is { } employee) q = q.Where(x => x.EmployeeId == employee);
        if (query.Status is { } status) q = q.Where(x => x.Status == status);
        return await q.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id)
            .Skip(query.Skip).Take(query.Take).ToListAsync(ct);
    }

    public async Task SaveAsync(SupplyRequest request, CancellationToken ct)
    {
        Session.Store(request);
        await Session.SaveChangesAsync(ct);
    }
}
