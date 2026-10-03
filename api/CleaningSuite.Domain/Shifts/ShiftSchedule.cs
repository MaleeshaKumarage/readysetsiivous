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
}
