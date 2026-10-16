using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

public record ResolvedShiftOccurrence(DateTime StartUtc, DateTime EndUtc,
    Guid? EmployeeId = null, ShiftOccurrenceExceptionStatus? Status = null,
    Guid? ExceptionId = null, DateTime? OriginalStartUtc = null);

public static class ShiftOccurrenceResolver
{
    public static IReadOnlyList<ResolvedShiftOccurrence> Apply(
        IEnumerable<ShiftOccurrence> generated,
        IEnumerable<ShiftOccurrenceException> exceptions,
        bool includeCancelled = false)
    {
        var changes = exceptions.ToList();
        var byOriginal = changes.Where(x => x.OriginalStartUtc.HasValue)
            .GroupBy(x => Utc(x.OriginalStartUtc!.Value))
            .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.UpdatedUtc).First());
        var result = new List<ResolvedShiftOccurrence>();
        var consumed = new HashSet<Guid>();
        foreach (var occurrence in generated)
        {
            var original = Utc(occurrence.StartUtc);
            if (!byOriginal.TryGetValue(original, out var change))
            {
                result.Add(new(original, Utc(occurrence.EndUtc)));
                continue;
            }
            consumed.Add(change.Id);
            if (!includeCancelled && change.Status is ShiftOccurrenceExceptionStatus.CustomerCancelled or ShiftOccurrenceExceptionStatus.CompanyCancelled)
                continue;
            result.Add(Map(change));
        }
        // A moved visit can fall into a query window that does not include its original
        // generated date. Include such unmatched exceptions exactly once in the target window.
        result.AddRange(changes.Where(x => !consumed.Contains(x.Id)
            && (!x.OriginalStartUtc.HasValue || includeCancelled
                || x.Status is not (ShiftOccurrenceExceptionStatus.CustomerCancelled or ShiftOccurrenceExceptionStatus.CompanyCancelled)))
            .Select(Map));
        return result.OrderBy(x => x.StartUtc).ToArray();
    }

    private static ResolvedShiftOccurrence Map(ShiftOccurrenceException item) =>
        new(Utc(item.StartUtc), Utc(item.EndUtc), item.EmployeeId, item.Status,
            item.Id, item.OriginalStartUtc.HasValue ? Utc(item.OriginalStartUtc.Value) : null);

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
