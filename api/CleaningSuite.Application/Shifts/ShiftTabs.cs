using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Marten;

namespace CleaningSuite.Application.Shifts;

/// <summary>The admin list tab a shift belongs to. Stored on the shift so the list can page in the database.</summary>
public static class ShiftTabs
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Inactive = "inactive";
    public static readonly IReadOnlyList<string> All = [Active, Completed, Inactive];

    /// <summary>Recomputes the stored tab and checklist state, then stores the shift in the caller's session so one commit saves both.</summary>
    public static async Task StoreAsync(IDocumentSession session, Shift shift, CancellationToken ct)
    {
        var completed = shift.Schedule.IsOneTime && await session.Query<QualityCycleForm>()
            .AnyAsync(f => f.ShiftId == shift.Id && f.IsSubmitted && f.ShiftDeletedUtc == null, ct);
        shift.ChecklistCompleted = completed;
        shift.Tab = !shift.IsActive ? Inactive : completed ? Completed : Active;
        session.Store(shift);
    }
}
