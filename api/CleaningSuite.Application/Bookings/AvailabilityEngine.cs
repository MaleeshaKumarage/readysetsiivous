using CleaningSuite.Application.Common;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Services;
using TimeZoneConverter;

namespace CleaningSuite.Application.Bookings;

public record AvailabilitySlot(string StartTime, string EndTime);

/// <summary>
/// Computes bookable slots for a local date: business window minus occupied
/// bookings. MVP: single-team model — any active booking blocks its window,
/// regardless of employee assignment (employee schedules come in a later phase).
/// </summary>
public class AvailabilityEngine
{
    // MVP business window; configurable per tenant in a later phase.
    private static readonly TimeSpan WindowStart = TimeSpan.FromHours(8);
    private static readonly TimeSpan WindowEnd = TimeSpan.FromHours(20);
    private const int SlotMinutes = 30;

    private readonly IServiceRepository _services;
    private readonly IBookingRepository _bookings;
    private readonly ITenantProfileRepository _profiles;

    public AvailabilityEngine(
        IServiceRepository services,
        IBookingRepository bookings,
        ITenantProfileRepository profiles)
    {
        _services = services;
        _bookings = bookings;
        _profiles = profiles;
    }

    private static TimeZoneInfo TimeZoneFor(string timeZoneId) =>
        TZConvert.GetTimeZoneInfo(string.IsNullOrEmpty(timeZoneId) ? "Europe/Helsinki" : timeZoneId);

    public async Task<IReadOnlyList<AvailabilitySlot>> GetSlotsAsync(
        string tenantId, string localDate, Guid serviceId, CancellationToken ct)
    {
        var service = await _services.GetByIdAsync(serviceId, ct)
            ?? throw new NotFoundException("Service", serviceId);

        var profile = await _profiles.GetAsync(tenantId, ct);
        var tz = TimeZoneFor(profile?.TimeZoneId ?? "");
        var date = DateOnly.ParseExact(localDate, "yyyy-MM-dd");
        var occupied = await _bookings.ListForLocalDateAsync(localDate, ct);

        var slots = new List<AvailabilitySlot>();
        for (var cursor = WindowStart; cursor + TimeSpan.FromMinutes(service.DurationMinutes) <= WindowEnd;
             cursor += TimeSpan.FromMinutes(SlotMinutes))
        {
            var slotStartLocal = date.ToDateTime(TimeOnly.FromTimeSpan(cursor));
            var slotEndLocal = slotStartLocal.AddMinutes(service.DurationMinutes);
            var slotStartUtc = TimeZoneInfo.ConvertTimeToUtc(slotStartLocal, tz);
            var slotEndUtc = TimeZoneInfo.ConvertTimeToUtc(slotEndLocal, tz);

            var conflicts = occupied.Any(b => b.StartUtc < slotEndUtc && b.EndUtc > slotStartUtc);
            if (!conflicts)
                slots.Add(new AvailabilitySlot(
                    cursor.ToString(@"hh\:mm"),
                    (cursor + TimeSpan.FromMinutes(service.DurationMinutes)).ToString(@"hh\:mm")));
        }

        return slots;
    }

    /// <summary>Validates a requested slot and returns UTC start/end. Throws SlotConflictException on any violation.</summary>
    public async Task<(DateTime StartUtc, DateTime EndUtc)> ReserveAsync(
        string tenantId,
        string localDate,
        string startTime,
        Service service,
        Guid? exceptBookingId = null,
        CancellationToken ct = default)
    {
        var profile = await _profiles.GetAsync(tenantId, ct);
        var tz = TimeZoneFor(profile?.TimeZoneId ?? "");
        var date = DateOnly.ParseExact(localDate, "yyyy-MM-dd");
        var time = TimeOnly.ParseExact(startTime, "HH:mm");

        var startLocal = date.ToDateTime(time);
        var endLocal = startLocal.AddMinutes(service.DurationMinutes);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, tz);

        var minHours = profile?.MinHoursBeforeBooking ?? 2;
        if (startUtc < DateTime.UtcNow.AddHours(minHours))
            throw new SlotConflictException($"Booking must be at least {minHours} hours in the future");

        if (time.ToTimeSpan() < WindowStart || endLocal.TimeOfDay > WindowEnd)
            throw new SlotConflictException("Slot is outside business hours");

        if (!service.IsActive)
            throw new SlotConflictException("Service is not active");

        var overlaps = await _bookings.FindOverlappingAsync(startUtc, endUtc, exceptBookingId, ct);
        if (overlaps.Count > 0)
            throw new SlotConflictException("Slot is already taken");

        return (startUtc, endUtc);
    }
}

public class SlotConflictException(string message) : Exception(message);
