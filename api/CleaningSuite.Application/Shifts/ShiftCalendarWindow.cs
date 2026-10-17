using System.Globalization;
using FluentValidation;

namespace CleaningSuite.Application.Shifts;

public record ShiftCalendarWindow(DateTime FromUtc, DateTime ToUtc, string FromLocalDate, string ToLocalDate)
{
    public static ShiftCalendarWindow Create(string view, string? cursor, DateTime nowUtc, TimeZoneInfo zone)
    {
        var today = TenantTimeZone.LocalDate(nowUtc, zone);
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
