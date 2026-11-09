using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Employees;

/// <summary>
/// One dated hourly rate for an employee. Entries are append-only: a new rate never edits an earlier one,
/// so work already calculated keeps the rate that applied on its scheduled date.
/// </summary>
public class EmployeePayRate : BaseDocument
{
    public const string SourceManual = "manual";
    public const string SourceLegacyConfirmed = "legacy-confirmed";

    public Guid EmployeeId { get; set; }
    public decimal HourlyRate { get; set; }
    /// <summary>First local calendar date (tenant timezone) on which this rate applies.</summary>
    public DateOnly EffectiveFrom { get; set; }
    public string Source { get; set; } = SourceManual;
    public string RecordedBySubject { get; set; } = "";
}
