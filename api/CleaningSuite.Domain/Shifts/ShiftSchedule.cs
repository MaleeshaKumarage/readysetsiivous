namespace CleaningSuite.Domain.Shifts;

public enum ShiftScheduleType
{
    DailySameTime = 0,
    DailyDifferentTime = 1,
    Weekly = 2,
    BiWeekly = 3,
    OnCallFlexible = 4
}

public class TimeRange
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public bool IsValid => End > Start;
}

public class ShiftSchedule
{
    public ShiftScheduleType Type { get; set; }
    public TimeSpan? DailyStart { get; set; }
    public TimeSpan? DailyEnd { get; set; }
    public Dictionary<DayOfWeek, TimeRange> DailyTimes { get; set; } = new();
    public DayOfWeek? WeeklyDay { get; set; }
    public TimeSpan? WeeklyStart { get; set; }
    public TimeSpan? WeeklyEnd { get; set; }
    public int? BiWeeklyWeekParity { get; set; }
    public DayOfWeek? BiWeeklyDay { get; set; }
    public TimeSpan? BiWeeklyStart { get; set; }
    public TimeSpan? BiWeeklyEnd { get; set; }
    public bool OnCall => Type == ShiftScheduleType.OnCallFlexible;

    /// <summary>
    /// Ensures the schedule is fully populated for its <see cref="Type"/> so that a
    /// misconfigured schedule fails fast instead of silently producing zero occurrences.
    /// </summary>
    public void Validate()
    {
        switch (Type)
        {
            case ShiftScheduleType.DailySameTime:
                if (DailyStart is null || DailyEnd is null)
                    throw new ArgumentException(
                        "DailySameTime schedules require both DailyStart and DailyEnd.",
                        nameof(DailyStart));
                if (DailyEnd <= DailyStart)
                    throw new ArgumentException(
                        "DailyEnd must be after DailyStart.", nameof(DailyEnd));
                break;

            case ShiftScheduleType.DailyDifferentTime:
                if (DailyTimes is null || DailyTimes.Count == 0)
                    throw new ArgumentException(
                        "DailyDifferentTime schedules require at least one entry in DailyTimes.",
                        nameof(DailyTimes));
                foreach (var (day, range) in DailyTimes)
                {
                    if (range is null)
                        throw new ArgumentException(
                            $"DailyTimes is missing a time range for {day}.", nameof(DailyTimes));
                    if (!range.IsValid)
                        throw new ArgumentException(
                            $"DailyTimes end time for {day} must be after its start time.",
                            nameof(DailyTimes));
                }
                break;

            case ShiftScheduleType.Weekly:
                if (WeeklyDay is null || WeeklyStart is null || WeeklyEnd is null)
                    throw new ArgumentException(
                        "Weekly schedules require WeeklyDay, WeeklyStart and WeeklyEnd.",
                        nameof(WeeklyDay));
                if (WeeklyEnd <= WeeklyStart)
                    throw new ArgumentException(
                        "WeeklyEnd must be after WeeklyStart.", nameof(WeeklyEnd));
                break;

            case ShiftScheduleType.BiWeekly:
                if (BiWeeklyWeekParity is null || BiWeeklyDay is null
                    || BiWeeklyStart is null || BiWeeklyEnd is null)
                    throw new ArgumentException(
                        "BiWeekly schedules require BiWeeklyWeekParity, BiWeeklyDay, BiWeeklyStart and BiWeeklyEnd.",
                        nameof(BiWeeklyWeekParity));
                if (BiWeeklyEnd <= BiWeeklyStart)
                    throw new ArgumentException(
                        "BiWeeklyEnd must be after BiWeeklyStart.", nameof(BiWeeklyEnd));
                break;

            case ShiftScheduleType.OnCallFlexible:
                // No fixed times; nothing to validate.
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(Type), Type, "Unknown shift schedule type.");
        }
    }
}
