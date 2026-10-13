using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.QualityCycle;

/// <summary>
/// Creates Quality Cycle forms (and emails fill-in links) for the given shift
/// occurrences, for every assigned active employee. Idempotent: skips
/// (shift, employee, occurrence) combinations that already have a form.
/// </summary>
public interface IQualityCycleDispatcher
{
    Task<int> EnsureUpcomingAsync(Guid? shiftId = null, CancellationToken ct = default);
    Task<int> DispatchAsync(
        Shift shift,
        IReadOnlyList<ShiftOccurrence> occurrences,
        CancellationToken ct = default);
}
