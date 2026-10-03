using System;
using System.Collections.Generic;
using System.Linq;

namespace CleaningSuite.Domain.Shifts;

public static class ShiftScheduleCalculator
{
    public static IReadOnlyList<ShiftOccurrence> GenerateOccurrences(
        Shift shift,
        DateTime from,
        DateTime to)
    {
        if (shift is null)
            throw new ArgumentNullException(nameof(shift));
        if (!shift.IsActive)
            return Array.Empty<ShiftOccurrence>();
        if (from >= to)
            return Array.Empty<ShiftOccurrence>();

        var result = new List<ShiftOccurrence>();
        var schedule = shift.Schedule;
        var startDate = from.Date;
        var endDate = to.Date;

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var weekday = date.DayOfWeek;
            switch (schedule.Type)
            {
                case ShiftScheduleType.DailySameTime:
                    if (schedule.DailyStart.HasValue && schedule.DailyEnd.HasValue)
                        AddOccurrence(result, date, schedule.DailyStart.Value, schedule.DailyEnd.Value);
                    break;

                case ShiftScheduleType.DailyDifferentTime:
                    if (schedule.DailyTimes.TryGetValue(weekday, out var daily))
                        AddOccurrence(result, date, daily.Start, daily.End);
                    break;

                case ShiftScheduleType.Weekly:
                    if (schedule.WeeklyDay == weekday && schedule.WeeklyStart.HasValue && schedule.WeeklyEnd.HasValue)
                        AddOccurrence(result, date, schedule.WeeklyStart.Value, schedule.WeeklyEnd.Value);
                    break;

                case ShiftScheduleType.BiWeekly:
                    if (schedule.BiWeeklyDay == weekday &&
                        schedule.BiWeeklyStart.HasValue &&
                        schedule.BiWeeklyEnd.HasValue)
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
                    break;

                case ShiftScheduleType.OnCallFlexible:
                    break;
            }
        }

        return result;
    }

    public static bool Overlaps(ShiftOccurrence a, ShiftOccurrence b)
    {
        if (a is null || b is null)
            return false;
        return a.StartUtc < b.EndUtc && b.StartUtc < a.EndUtc;
    }

    public static bool HasOverlap(
        IEnumerable<ShiftOccurrence> candidate,
        IEnumerable<ShiftOccurrence> existing)
    {
        return candidate.Any(c => existing.Any(e => Overlaps(c, e)));
    }

    private static void AddOccurrence(
        List<ShiftOccurrence> result,
        DateTime date,
        TimeSpan start,
        TimeSpan end)
    {
        if (end <= start)
            return;
        result.Add(new ShiftOccurrence
        {
            StartUtc = date + start,
            EndUtc = date + end
        });
    }
}
