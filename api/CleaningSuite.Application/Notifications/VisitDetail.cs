using System.Globalization;

namespace CleaningSuite.Application.Notifications;

/// <summary>
/// Short "which visit" text added to an alert: the property and the local start time when known.
/// Example: "Visit: Lakeside Villa, 14 Nov 08:00." Returns null when there is nothing to say.
/// </summary>
public static class VisitDetail
{
    public static string? Describe(string? property, DateTime? startUtc, TimeZoneInfo? zone)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(property)) parts.Add(property.Trim());
        if (startUtc is { } start && zone is not null)
        {
            var utc = DateTime.SpecifyKind(start, DateTimeKind.Utc);
            parts.Add(TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("d MMM HH:mm", CultureInfo.GetCultureInfo("en-GB")));
        }
        return parts.Count == 0 ? null : $"Visit: {string.Join(", ", parts)}.";
    }
}
