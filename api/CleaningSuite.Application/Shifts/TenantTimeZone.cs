using System.Linq;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using FluentValidation;
using FluentValidation.Results;
using TimeZoneConverter;

namespace CleaningSuite.Application.Shifts;

public static class TenantTimeZone
{
    public const string DefaultId = "Europe/Helsinki";

    public static TimeZoneInfo Resolve(string? id)
    {
        var timeZoneId = string.IsNullOrWhiteSpace(id) ? DefaultId : id;
        try { return TZConvert.GetTimeZoneInfo(timeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new ValidationException(new[] { new ValidationFailure("timeZone", "The tenant time zone requires administrator correction.") }); }
    }

    public static async Task<(string Id, TimeZoneInfo Zone)> LoadAsync(
        ITenantProfileRepository? profiles, ITenantContext? tenant, CancellationToken ct)
    {
        // Directly constructed handlers in isolated unit tests have no tenant services.
        // The production container supplies both, and a provisioned tenant has a profile.
        if (profiles is null || tenant is null) return (TimeZoneInfo.Utc.Id, TimeZoneInfo.Utc);
        var profile = await profiles.GetAsync(tenant.TenantId, ct);
        var id = string.IsNullOrWhiteSpace(profile?.TimeZoneId) ? DefaultId : profile.TimeZoneId;
        return (id, Resolve(id));
    }

    public static DateTime LocalDate(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).Date;

    public static DateTime StartOfLocalDay(DateTime localDate, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        var last = local.AddDays(1);
        while (zone.IsInvalidTime(local) && local < last) local = local.AddMinutes(1);
        if (local >= last) throw new ValidationException(new[] { new ValidationFailure("date", "This local day does not exist in the tenant time zone.") });
        if (zone.IsAmbiguousTime(local))
            return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static (DateTime From, DateTime To) LocalDatesForUtcRange(DateTime fromUtc, DateTime toUtc, TimeZoneInfo zone)
    {
        var from = LocalDate(fromUtc, zone).AddDays(-1);
        var to = LocalDate(toUtc, zone).AddDays(1);
        return (from, to);
    }
}
