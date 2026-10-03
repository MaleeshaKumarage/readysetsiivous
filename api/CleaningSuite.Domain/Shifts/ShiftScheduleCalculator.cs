// Fixed, documented anchor: the Monday of ISO week 1, 2024. Week parity is
// measured relative to this date so results are stable and reproducible.
var biWeeklyAnchor = new DateTime(2024, 1, 1);
var weekIndex = (int)Math.Floor((date - biWeeklyAnchor).TotalDays / 7.0);
// Modulo-safe: yields 1 or 2 for both positive and negative weekIndex values.
var parity = ((weekIndex % 2) + 2) % 2 + 1;
if (parity == schedule.BiWeeklyWeekParity)
    AddOccurrence(result, date, schedule.BiWeeklyStart.Value, schedule.BiWeeklyEnd.Value);
