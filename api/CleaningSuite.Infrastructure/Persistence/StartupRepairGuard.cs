using CleaningSuite.Domain.Tenants;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>Runs a one-time startup repair once per tenant, using <see cref="StartupRepairMarker"/>.</summary>
public static class StartupRepairGuard
{
    public static async Task<bool> IsCompleteAsync(IDocumentStore store, string tenant, string repair, CancellationToken ct)
    {
        using var session = store.QuerySession(tenant);
        return await session.LoadAsync<StartupRepairMarker>(StartupRepairMarker.IdFor(repair), ct) is not null;
    }

    public static async Task MarkCompleteAsync(IDocumentStore store, string tenant, string repair, string summary, CancellationToken ct)
    {
        using var session = store.LightweightSession(tenant);
        session.Store(new StartupRepairMarker { Id = StartupRepairMarker.IdFor(repair), Repair = repair, CompletedUtc = DateTime.UtcNow, Summary = summary });
        await session.SaveChangesAsync(ct);
    }
}
