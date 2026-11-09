using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Domain.Employees;

namespace CleaningSuite.Application.Employees.PayRates;

public static class EmployeePayRateRules
{
    public const decimal MaxHourlyRate = 10000m;

    /// <summary>Returns a user-facing reason when the rate cannot be stored, otherwise null.</summary>
    public static string? ValidateRate(decimal rate)
    {
        if (rate < 0) return "Hourly rate cannot be negative. Enter 0 or a positive amount.";
        if (rate > MaxHourlyRate) return $"Hourly rate must be {MaxHourlyRate:0} or less.";
        if (decimal.Round(rate, 2) != rate) return "Hourly rate can have at most two decimal places.";
        return null;
    }

    /// <summary>
    /// The rate for a local work date is the latest entry whose start date is on or before that date.
    /// Null means no rate covers the date; callers must treat that as an exception, never as zero pay.
    /// </summary>
    public static EmployeePayRate? EffectiveOn(IEnumerable<EmployeePayRate> entries, DateOnly workDate) =>
        entries.Where(entry => entry.EffectiveFrom <= workDate)
            .OrderByDescending(entry => entry.EffectiveFrom)
            .FirstOrDefault();

    /// <summary>
    /// The legacy Employee.PayRate may be confirmed once, and only as the earliest dated rate, so it can never
    /// overwrite or reorder a rate the admin entered on purpose.
    /// </summary>
    public static string? LegacyConfirmationRefusal(IReadOnlyList<EmployeePayRate> entries, DateOnly effectiveFrom)
    {
        if (entries.Any(entry => entry.Source == EmployeePayRate.SourceLegacyConfirmed))
            return "The legacy pay rate has already been confirmed.";
        if (entries.Count > 0 && effectiveFrom >= entries.Min(entry => entry.EffectiveFrom))
            return "The legacy pay rate must start before the dated rates already recorded.";
        return null;
    }

    /// <summary>
    /// Deterministic identity per employee and effective date. Inserting a second entry for the same date fails,
    /// which makes duplicate rates impossible even when two admin requests race.
    /// </summary>
    public static Guid IdFor(Guid employeeId, DateOnly effectiveFrom)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"employee-pay-rate:{employeeId:N}:{effectiveFrom:yyyy-MM-dd}"));
        return new Guid(hash[..16]);
    }
}
