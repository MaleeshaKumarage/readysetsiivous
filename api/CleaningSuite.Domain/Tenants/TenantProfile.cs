using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Tenants;

/// <summary>
/// Per-tenant company settings, dynamic site content and invoice issuer data.
/// Stored in the tenant's own partition.
/// </summary>
public class TenantProfile : BaseDocument
{
    public string Slug { get; set; } = "";
    public string RealmName { get; set; } = "";
    public string CompanyName { get; set; } = "";

    /// <summary>Finnish business id (Y-tunnus).</summary>
    public string BusinessId { get; set; } = "";

    public Address CompanyAddress { get; set; } = new();
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string BankAccountIBAN { get; set; } = "";
    public string BankBic { get; set; } = "";
    public string? LogoUrl { get; set; }

    /// <summary>IANA time zone id, e.g. Europe/Helsinki.</summary>
    public string TimeZoneId { get; set; } = "Europe/Helsinki";

    public string DefaultLocale { get; set; } = "fi";
    public decimal DefaultVatRatePercent { get; set; } = 25.5m;
    public int PaymentTermsDays { get; set; } = 14;

    /// <summary>Public bookings may be created without an assigned employee.</summary>
    public bool AllowUnstaffedBookings { get; set; } = true;

    public int MinHoursBeforeBooking { get; set; } = 2;

    /// <summary>Dynamic public page content. Keys: home, services, about, faq, footer.</summary>
    public Dictionary<string, LocalizedText> Pages { get; set; } = new();
}
