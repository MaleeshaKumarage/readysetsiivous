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
    /// Uses a two-pointer sweep-line algorithm with O(M log M + N log N) complexity instead of O(M * N)
    /// nested loops, significantly speeding up employee shift collision checks over large date ranges.
    /// </summary>
    public static bool HasOverlap(
        IReadOnlyList<ShiftOccurrence> first,
        IReadOnlyList<ShiftOccurrence> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));

        if (first.Count == 0 || second.Count == 0)
            return false;

        // Ensure lists are ordered by start time for sweep-line algorithm
        var listA = EnsureSorted(first);
        var listB = EnsureSorted(second);

        int i = 0, j = 0;
        while (i < listA.Count && j < listB.Count)
        {
            var a = listA[i];
            var b = listB[j];

            // Check if intervals [a.StartUtc, a.EndUtc) and [b.StartUtc, b.EndUtc) overlap
            if (a.StartUtc < b.EndUtc && b.StartUtc < a.EndUtc)
            {
                return true;
            }

            // Advance pointer of the interval that ends earlier
            if (a.EndUtc <= b.EndUtc)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return false;
    }

    private static IReadOnlyList<ShiftOccurrence> EnsureSorted(IReadOnlyList<ShiftOccurrence> list)
    {
        for (int k = 1; k < list.Count; k++)
        {
            if (list[k].StartUtc < list[k - 1].StartUtc)
            {
                var sorted = new List<ShiftOccurrence>(list);
                sorted.Sort((x, y) => x.StartUtc.CompareTo(y.StartUtc));
                return sorted;
            }
        }
        return list;
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
