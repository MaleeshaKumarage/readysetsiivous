using CleaningSuite.Domain.Invoicing;

namespace CleaningSuite.Application.Invoicing;

public interface IShiftBillingRepository
{
    Task<ShiftBillingProfile?> GetAsync(Guid shiftId, CancellationToken ct = default);
    Task SaveAsync(ShiftBillingProfile profile, CancellationToken ct = default);
}
