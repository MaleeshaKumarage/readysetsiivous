using System;
using System.Collections.Generic;

namespace CleaningSuite.Domain.Shifts;

public static class ShiftScheduleCalculator
{
    /// <summary>
    /// Expands a bi-weekly <see cref="ShiftSchedule"/> into concrete occurrences
    /// between <paramref name="from"/> and <paramref name="to"/> (inclusive).
    /// </summary>
    public static IReadOnlyList<ShiftOccurrence> Calculate(
        ShiftSchedule schedule,
        DateTime from,
        DateTime to)
    {
        if (schedule is null) throw new ArgumentNullException(nameof(schedule));
        if (schedule.BiWeeklyStart is null || schedule.BiWeeklyEnd is null)
            return Array.Empty<ShiftOccurrence>();

        var result = new List<ShiftOccurrence>();

        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            // Fixed, documented anchor: the Monday of ISO week 1, 2024. Week parity is
            // measured relative to this date so results are stable and reproducible.
            var biWeeklyAnchor = new DateTime(2024, 1, 1);
            var weekIndex = (int)Math.Floor((date - biWeeklyAnchor).TotalDays / 7.0);
            // Modulo-safe: yields 1 or 2 for both positive and negative weekIndex values.
            var parity = ((weekIndex % 2) + 2) % 2 + 1;
            if (parity == schedule.BiWeeklyWeekParity)
                AddOccurrence(result, date, schedule.BiWeeklyStart.Value, schedule.BiWeeklyEnd.Value);
        }

        return result;
    }

    /// <summary>
    /// Expands the schedule of a <see cref="Shift"/> into concrete occurrences
    /// between <paramref name="from"/> and <paramref name="to"/> (inclusive).
    /// </summary>
    public static IReadOnlyList<ShiftOccurrence> GenerateOccurrences(
        Shift shift,
        DateTime from,
        DateTime to)
    {
        if (shift is null) throw new ArgumentNullException(nameof(shift));
        return GenerateOccurrences(shift.Schedule, from, to);
    }

    /// <summary>
    /// Expands a <see cref="ShiftSchedule"/> into concrete occurrences
    /// between <paramref name="from"/> and <paramref name="to"/> (inclusive).
    /// </summary>
    public static IReadOnlyList<ShiftOccurrence> GenerateOccurrences(
        ShiftSchedule schedule,
        DateTime from,
        DateTime to)
        => Calculate(schedule, from, to);

    /// <summary>
    /// Returns true when any occurrence in <paramref name="first"/> overlaps any occurrence in
    /// <paramref name="second"/>. Touching boundaries (one occurrence ending exactly when the
    /// next starts) are not treated as overlaps.
    /// </summary>
    public static bool HasOverlap(
        IReadOnlyList<ShiftOccurrence> first,
        IReadOnlyList<ShiftOccurrence> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));

        foreach (var a in first)
        {
            foreach (var b in second)
            {
                if (a.StartUtc < b.EndUtc && b.StartUtc < a.EndUtc)
                    return true;
            }
        }

        return false;
    }

    private static void AddOccurrence(
        List<ShiftOccurrence> result,
        DateTime date,
        TimeSpan start,
        TimeSpan end)
    {
        result.Add(new ShiftOccurrence
        {
            StartUtc = DateTime.SpecifyKind(date.Date + start, DateTimeKind.Utc),
            EndUtc = DateTime.SpecifyKind(date.Date + end, DateTimeKind.Utc),
        });
    }
}
