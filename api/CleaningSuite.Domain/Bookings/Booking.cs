using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Bookings;

public class Booking : BaseDocument
{
    public const string StatusNew = "New";
    public const string StatusConfirmed = "Confirmed";
    public const string StatusCancelled = "Cancelled";
    public const string StatusCompleted = "Completed";

    public string BookingNumber { get; set; } = "";
    public Customer Customer { get; set; } = new();
    public Address CleaningAddress { get; set; } = new();

    public string ServiceId { get; set; } = "";
    public ServiceSnapshot Service { get; set; } = new();

    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>Server-computed local strings for timezone-safe listing, e.g. "2026-09-12", "10:00".</summary>
    public string StartLocalDate { get; set; } = "";

    public string StartLocalTime { get; set; } = "";

    public Money Total { get; set; } = new();
    public string Status { get; set; } = StatusNew;

    /// <summary>Public | Admin</summary>
    public string? Source { get; set; }

    /// <summary>Self-service lookup code shown to the customer.</summary>
    public string? CustomerReference { get; set; }

    public string? Notes { get; set; }
    public List<StatusEvent> History { get; set; } = [];
}

/// <summary>Customer contact info.</summary>
public class Customer
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
}

/// <summary>Service name and price frozen at booking time. Later price edits never rewrite history.</summary>
public class ServiceSnapshot
{
    public string NameFi { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameSv { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal PriceNet { get; set; }
    public decimal VatRatePercent { get; set; }
}

/// <summary>Audit trail entry.</summary>
public class StatusEvent
{
    public string Status { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string By { get; set; } = "";
    public string? Note { get; set; }
}
