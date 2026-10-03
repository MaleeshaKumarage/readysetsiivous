/// <summary>Start/end time pair for a weekday working window. Null end means closed.</summary>
public class WorkHours
{
    public TimeSpan? Start { get; set; }
    public TimeSpan? End { get; set; }

    /// <summary>True when this window is closed (no end time).</summary>
    public bool IsClosed => End is null;
}
