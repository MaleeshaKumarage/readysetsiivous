using CleaningSuite.Application.Inventory;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Inventory;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public sealed class InventoryRepository(ITenantSession tenant) : IInventoryRepository
{
    private IDocumentSession Session => tenant.Session;

    public async Task<IReadOnlyList<InventoryLocation>> ListLocationsAsync(CancellationToken ct) =>
        await Session.Query<InventoryLocation>().Take(1000).ToListAsync(ct);

    public async Task<IReadOnlyList<InventoryItem>> ListItemsAsync(CancellationToken ct) =>
        await Session.Query<InventoryItem>().Take(1000).ToListAsync(ct);

    public Task<InventoryLocation?> GetLocationAsync(Guid id, CancellationToken ct) => Session.LoadAsync<InventoryLocation>(id, ct);

    public Task<InventoryItem?> GetItemAsync(Guid id, CancellationToken ct) => Session.LoadAsync<InventoryItem>(id, ct);

    public Task<InventoryUsage?> GetUsageAsync(Guid id, CancellationToken ct) => Session.LoadAsync<InventoryUsage>(id, ct);

    public async Task<IReadOnlyList<InventoryUsage>> ListUsageAsync(InventoryUsageQuery query, CancellationToken ct)
    {
        IQueryable<InventoryUsage> q = Session.Query<InventoryUsage>();
        if (query.EmployeeId is { } employee) q = q.Where(x => x.EmployeeId == employee);
        if (query.FromUtc is { } from) q = q.Where(x => x.TakenUtc >= from);
        if (query.ToUtc is { } to) q = q.Where(x => x.TakenUtc < to);
        return await q.OrderByDescending(x => x.TakenUtc).ThenBy(x => x.Id)
            .Skip(query.Skip).Take(query.Take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InventoryUsage>> ListUsageWithPhotosTakenBeforeAsync(DateTime cutoffUtc, int take, CancellationToken ct) =>
        await Session.Query<InventoryUsage>()
            .Where(x => x.TakenUtc < cutoffUtc && x.PhotosPurgedUtc == null)
            .OrderBy(x => x.TakenUtc).Take(take).ToListAsync(ct);

    public async Task SaveAsync<T>(T document, CancellationToken ct) where T : BaseDocument
    {
        Session.Store(document);
        await Session.SaveChangesAsync(ct);
    }
}
