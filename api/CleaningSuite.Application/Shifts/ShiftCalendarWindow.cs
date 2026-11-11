using System.Globalization;
using FluentValidation;

namespace CleaningSuite.Application.Shifts;

public record ShiftCalendarWindow(DateTime FromUtc, DateTime ToUtc, string FromLocalDate, string ToLocalDate)
{
    // Completed work between two local dates, both inclusive. Past work only, at most 366 days.
    private static ShiftCalendarWindow FilteredPast(string? fromDate, string? toDate, DateTime today, TimeZoneInfo zone)
    {
        var to = toDate is null ? today : ParseDate(toDate);
        var from = fromDate is null ? to.AddDays(-31) : ParseDate(fromDate);
        if (to > today) throw new ValidationException("Past shifts end today or earlier. Choose an end date that has already passed.");
        if (from > to) throw new ValidationException("The start date must be on or before the end date.");
        if ((to - from).TotalDays > 365) throw new ValidationException("Choose a date range of up to one year.");
        return new(TenantTimeZone.StartOfLocalDay(from, zone), TenantTimeZone.StartOfLocalDay(to.AddDays(1), zone),
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static DateTime ParseDate(string value)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || parsed.Year is < 1971 or > 9997)
            throw new ValidationException("Choose a supported calendar date in yyyy-MM-dd format.");
        return parsed;
    }

    public static ShiftCalendarWindow Create(string view, string? cursor, DateTime nowUtc, TimeZoneInfo zone,
        string? fromDate = null, string? toDate = null)
    {
        var today = TenantTimeZone.LocalDate(nowUtc, zone);
        if (view == "past" && (fromDate is not null || toDate is not null)) return FilteredPast(fromDate, toDate, today, zone);
        DateTime? anchor = null;
        if (cursor is not null)
        {
            if (!DateTime.TryParseExact(cursor, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) || parsed.Year is < 1971 or > 9997)
                throw new ValidationException("Choose a supported calendar date in yyyy-MM-dd format.");
            anchor = parsed;
        }
        DateTime from, to;
        switch (view)
        {
            case "today" when anchor is null: from = today; to = today.AddDays(1); break;
            case "past" when anchor is null || anchor <= today:
                to = anchor ?? today; from = to.AddDays(-31); break;
            case "future" when anchor is null || anchor >= today.AddDays(1):
                from = anchor ?? today.AddDays(1); to = from.AddDays(31); break;
            default: throw new ValidationException("Choose Today, Past or Future with a date in that period.");
        }
        return new(TenantTimeZone.StartOfLocalDay(from, zone), TenantTimeZone.StartOfLocalDay(to, zone),
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
