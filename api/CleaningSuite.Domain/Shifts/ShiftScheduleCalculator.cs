using System;
using System.Collections.Generic;

namespace CleaningSuite.Domain.Shifts;

public static class ShiftScheduleCalculator
{
    /// <summary>
    /// Expands a <see cref="ShiftSchedule"/> into concrete occurrences
    /// between <paramref name="from"/> and <paramref name="to"/> (inclusive).
    /// </summary>
    public static IReadOnlyList<ShiftOccurrence> Calculate(
        ShiftSchedule schedule,
        DateTime from,
        DateTime to)
    {
        if (schedule is null) throw new ArgumentNullException(nameof(schedule));

        var result = new List<ShiftOccurrence>();

        switch (schedule.Type)
        {
            case ShiftScheduleType.DailySameTime:
                if (schedule.DailyStart is null || schedule.DailyEnd is null) break;
                for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
                    AddOccurrence(result, date, schedule.DailyStart.Value, schedule.DailyEnd.Value);
                break;

            case ShiftScheduleType.DailyDifferentTime:
                for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
                    if (schedule.DailyTimes.TryGetValue(date.DayOfWeek, out var range))
                        AddOccurrence(result, date, range.Start, range.End);
                break;

            case ShiftScheduleType.Weekly:
                if (schedule.WeeklyStart is null || schedule.WeeklyEnd is null) break;
                for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
                {
                    var isDay = schedule.WeeklyDays.Count > 0
                        ? schedule.WeeklyDays.Contains(date.DayOfWeek)
                        : date.DayOfWeek == schedule.WeeklyDay;
                    if (isDay)
                        AddOccurrence(result, date, schedule.WeeklyStart.Value, schedule.WeeklyEnd.Value);
                }
                break;

            case ShiftScheduleType.BiWeekly:
                {
                    if (schedule.BiWeeklyStart is null || schedule.BiWeeklyEnd is null) break;
                    var biWeeklyAnchor = new DateTime(2024, 1, 1);
                    for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
                    {
                        var weekIndex = (int)Math.Floor((date - biWeeklyAnchor).TotalDays / 7.0);
                        var parity = ((weekIndex % 2) + 2) % 2 + 1;
                        if (parity == schedule.BiWeeklyWeekParity && date.DayOfWeek == schedule.BiWeeklyDay)
                            AddOccurrence(result, date, schedule.BiWeeklyStart.Value, schedule.BiWeeklyEnd.Value);
                    }
                }
                break;

            case ShiftScheduleType.Monthly:
                if (schedule.MonthlyStart is null || schedule.MonthlyEnd is null) break;
                for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
                    if (date.Day == schedule.MonthlyDay)
                        AddOccurrence(result, date, schedule.MonthlyStart.Value, schedule.MonthlyEnd.Value);
                break;

            case ShiftScheduleType.OnCallFlexible:
                break;
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
    /// Uses an optimized 2-pointer scan after ensuring chronological order, reducing complexity
    /// from O(N*M) to O(N + M) for pre-sorted inputs.
    /// </summary>
    public static bool HasOverlap(
        IReadOnlyList<ShiftOccurrence> first,
        IReadOnlyList<ShiftOccurrence> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));

        if (first.Count == 0 || second.Count == 0)
            return false;

        var listA = EnsureSortedByStart(first);
        var listB = EnsureSortedByStart(second);

        int i = 0, j = 0;
        while (i < listA.Count && j < listB.Count)
        {
            var a = listA[i];
            var b = listB[j];

            // Overlap check for open intervals (StartUtc, EndUtc)
            if (a.StartUtc < b.EndUtc && b.StartUtc < a.EndUtc)
                return true;

            // Advance pointer for the interval ending earlier
            if (a.EndUtc <= b.EndUtc)
                i++;
            else
                j++;
        }

        return false;
    }

    private static IReadOnlyList<ShiftOccurrence> EnsureSortedByStart(IReadOnlyList<ShiftOccurrence> occurrences)
    {
        if (occurrences.Count <= 1)
            return occurrences;

        for (int k = 1; k < occurrences.Count; k++)
        {
            if (occurrences[k].StartUtc < occurrences[k - 1].StartUtc)
            {
                var copy = new List<ShiftOccurrence>(occurrences);
                copy.Sort((x, y) => x.StartUtc.CompareTo(y.StartUtc));
                return copy;
            }
        }

        return occurrences;
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
