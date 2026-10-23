using System.Globalization;
using FluentValidation;
namespace CleaningSuite.Application.Leads;

public static class LeadRecurrence
{
    public static DateTime Parse(string? value) => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && date.Year is >= 2000 and <= 9900 ? DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified)
        : throw new ValidationException("Choose a valid date between 2000 and 9900.");
    public static DateTime DueUtc(DateTime date, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.Date.AddHours(6), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return zone.IsAmbiguousTime(local) ? DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
    public static DateTime? Next(DateTime first, DateTime current, string recurrence, int interval)
    {
        try {
            var next = recurrence switch {
                "Once" => (DateTime?)null,
                "Daily" => current.AddDays(interval),
                "Weekly" => current.AddDays(7 * interval),
                "Monthly" => first.AddMonths((((current.Year - first.Year) * 12 + current.Month - first.Month) / interval + 1) * interval),
                _ => throw new ValidationException("Choose Once, Daily, Weekly or Monthly.")
            };
            return next?.Year > 9900 ? null : next;
        } catch (ArgumentOutOfRangeException) { return null; }
    }
}
