using CleaningSuite.Application.Invoicing;
using CleaningSuite.Domain.Invoicing;

namespace CleaningSuite.Infrastructure.Persistence;

public class ShiftBillingRepository(ITenantSession tenant) : IShiftBillingRepository
{
    public Task<ShiftBillingProfile?> GetAsync(Guid shiftId, CancellationToken ct = default) =>
        tenant.Session.LoadAsync<ShiftBillingProfile>(shiftId, ct);
    public async Task SaveAsync(ShiftBillingProfile profile, CancellationToken ct = default)
    {
        tenant.Session.Store(profile);
        await tenant.Session.SaveChangesAsync(ct);
    }
}
