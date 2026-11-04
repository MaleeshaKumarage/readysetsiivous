using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

/// <summary>Marks a one-time data correction as applied for one tenant, so a deployment never repeats it.</summary>
public class DataCorrectionRecord : BaseDocument
{
    public string Key { get; set; } = string.Empty;
    public DateTime AppliedUtc { get; set; }
    public int RemovedCount { get; set; }
    public int ReportedCount { get; set; }
}
