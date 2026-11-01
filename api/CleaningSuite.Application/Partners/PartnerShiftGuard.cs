using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Partners;

public static class PartnerShiftGuard
{
    public static void EnsureOrdinary(Shift shift)
    {
        if (shift.PartnerRequestId.HasValue)
            throw new ShiftConflictException("Manage this visit through Partner requests so that the cleaner accepts every assignment or time change.");
    }
}
