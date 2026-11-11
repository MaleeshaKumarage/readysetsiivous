using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Partners;

public static class PartnerShiftGuard
{
    private const string UsePartnerRequests = "Manage this visit through Partner requests so that assignment and time changes follow the employee work acceptance preference.";

    public static void EnsureOrdinary(Shift shift)
    {
        if (shift.PartnerRequestId.HasValue)
            throw new ShiftConflictException(UsePartnerRequests);
    }

    /// <summary>A partner booking's visit on a branch main shift is changed only through its request.</summary>
    public static void EnsureOrdinary(ShiftOccurrenceException? visit)
    {
        if (visit?.PartnerRequestId is not null)
            throw new ShiftConflictException(UsePartnerRequests);
    }

    /// <summary>
    /// A branch main shift cannot be switched off or removed while it still holds partner bookings: their cleaners
    /// would lose the visits while the requests stayed accepted.
    /// </summary>
    public static async Task EnsureNoBookingsAsync(IShiftRepository shifts, Shift shift, CancellationToken ct)
    {
        if (!shift.IsBranchMain) return;
        var visits = await shifts.ListOccurrenceExceptionsAsync(shift.Id, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2200, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct) ?? [];
        if (visits.Any(x => x.PartnerRequestId.HasValue))
            throw new ShiftConflictException("This shift holds accepted partner bookings. Cancel or move them under Partner requests first.");
    }

    /// <summary>A branch main shift stays an on-call shift of its own branch; its name, notes and checklist can change.</summary>
    public static void EnsureMainStaysPut(Shift shift, Guid companyId, Guid branchId, ShiftSchedule schedule)
    {
        if (shift.IsBranchMain && (shift.CompanyId != companyId || shift.BranchId != branchId || schedule.Type != ShiftScheduleType.OnCallFlexible))
            throw new ShiftConflictException("This is the property's main visit shift. Its company, property and on-call schedule cannot be changed.");
    }
}
