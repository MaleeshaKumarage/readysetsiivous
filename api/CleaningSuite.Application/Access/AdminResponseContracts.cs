using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;

namespace CleaningSuite.Application.Access;

// These allowlists are HTTP contracts, not persistence objects. Nested snapshots
// are explicit too: future domain fields must not silently broaden responses.
public record AddressResponse(string Street, string PostalCode, string City, string? Country);
public record CustomerAdminResponse(string Name, string Phone, string Email);
public record MoneyResponse(decimal Net, decimal Vat, decimal Gross);
public record HoursResponse(TimeSpan? Start, TimeSpan? End);
public record CertificationAdminResponse(string Name, DateTime? ExpiresAtUtc);
public record ServiceAdminResponse(string NameFi, string NameEn, string NameSv, int DurationMinutes, decimal PriceNet, decimal VatRatePercent);
public record StatusAdminResponse(string Status, DateTime AtUtc, string By, string? Note);
public record EmployeeAdminResponse(Guid Id, DateTime CreatedUtc, DateTime UpdatedUtc,
    string Email, string FirstName, string LastName, string Phone, string Role, bool IsActive,
    string? ColorHex, IReadOnlyList<string> Skills, IReadOnlyList<string> ServiceAreas, decimal? PayRate,
    IReadOnlyList<CertificationAdminResponse> Certifications, string? Notes,
    IReadOnlyDictionary<string, HoursResponse> DefaultHours, string KeycloakUserId,
    DateTime? InvitedAtUtc, DateTime? InviteLastAttemptUtc, DateTime? InviteLastSentUtc,
    string? InviteError, bool IdentitySyncPending);
public record BookingAdminResponse(Guid Id, DateTime CreatedUtc, DateTime UpdatedUtc, string BookingNumber,
    CustomerAdminResponse Customer, AddressResponse CleaningAddress, string ServiceId, ServiceAdminResponse Service,
    string? EmployeeId, string? EmployeeName, DateTime StartUtc, DateTime EndUtc, string StartLocalDate,
    string StartLocalTime, MoneyResponse Total, string Status, string? Source, string? CustomerReference,
    string? Notes, IReadOnlyList<StatusAdminResponse> History);
public record IssuerAdminResponse(string CompanyName, string BusinessId, AddressResponse CompanyAddress,
    string Email, string Phone, string BankAccountIBAN, string BankBic);
public record InvoiceLineAdminResponse(string Description, decimal Quantity, decimal UnitPriceNet,
    decimal VatRatePercent, decimal VatAmount, decimal Total)
{
    public Guid? ShiftId { get; init; }
    public string ShiftName { get; init; } = "";
    public string BranchName { get; init; } = "";
    public decimal Hours { get; init; }
    public int Occurrences { get; init; }
    public string PricingMode { get; init; } = "";
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public string LineKey { get; init; } = "";
    public string? Month { get; init; }
    public decimal NetAmount { get; init; }
    public string? OverrideReason { get; init; }
}
public record InvoiceAdminResponse(Guid Id, DateTime CreatedUtc, DateTime UpdatedUtc, string InvoiceNumber,
    Guid BookingId, string BookingNumber, CustomerAdminResponse Customer, IssuerAdminResponse Issuer,
    IReadOnlyList<InvoiceLineAdminResponse> Lines, MoneyResponse Total, string Currency,
    DateTime IssueDate, DateTime DueDate, string Status, DateTime? PaidAtUtc)
{
    public Guid? CompanyId { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public string Language { get; init; } = "fi";
    public AddressResponse? BillingAddress { get; init; }
    public string CustomerBusinessId { get; init; } = "";
    public string PaymentReference { get; init; } = "";
    public string? PreviewToken { get; init; }
    public bool IssuerIsSample { get; init; }
}

public static class AdminResponses
{
    public static AddressResponse Address(Address a) => new(a.Street, a.PostalCode, a.City, a.Country);
    private static CustomerAdminResponse Customer(Customer c) => new(c.Name, c.Phone, c.Email);
    private static MoneyResponse Money(Money m) => new(m.Net, m.Vat, m.Gross);
    public static IReadOnlyDictionary<string, HoursResponse> Hours(Dictionary<string, WorkHours> hours) =>
        hours.ToDictionary(x => x.Key, x => new HoursResponse(x.Value.Start, x.Value.End));
    public static EmployeeAdminResponse Employee(Employee e) => new(e.Id, e.CreatedUtc, e.UpdatedUtc,
        e.Email, e.FirstName, e.LastName, e.Phone, e.Role, e.IsActive, e.ColorHex, e.Skills.ToArray(),
        e.ServiceAreas.ToArray(), e.PayRate, e.Certifications.Select(c => new CertificationAdminResponse(c.Name, c.ExpiresAtUtc)).ToArray(),
        e.Notes, Hours(e.DefaultHours), e.KeycloakUserId, e.InvitedAtUtc, e.InviteLastAttemptUtc,
        e.InviteLastSentUtc, e.InviteError, e.IdentitySyncPending);
    public static BookingAdminResponse Booking(Booking b) => new(b.Id, b.CreatedUtc, b.UpdatedUtc,
        b.BookingNumber, Customer(b.Customer), Address(b.CleaningAddress), b.ServiceId,
        new(b.Service.NameFi, b.Service.NameEn, b.Service.NameSv, b.Service.DurationMinutes, b.Service.PriceNet, b.Service.VatRatePercent),
        b.EmployeeId, b.EmployeeName, b.StartUtc, b.EndUtc, b.StartLocalDate, b.StartLocalTime, Money(b.Total),
        b.Status, b.Source, b.CustomerReference, b.Notes,
        b.History.Select(h => new StatusAdminResponse(h.Status, h.AtUtc, h.By, h.Note)).ToArray());
    public static InvoiceAdminResponse Invoice(Invoice i) => new(i.Id, i.CreatedUtc, i.UpdatedUtc,
        i.InvoiceNumber, i.BookingId, i.BookingNumber, Customer(i.Customer),
        new(i.Issuer.CompanyName, i.Issuer.BusinessId, Address(i.Issuer.CompanyAddress), i.Issuer.Email,
            i.Issuer.Phone, i.Issuer.BankAccountIBAN, i.Issuer.BankBic),
        i.Lines.Select(l => new InvoiceLineAdminResponse(l.Description, l.Quantity, l.UnitPriceNet, l.VatRatePercent, l.VatAmount, l.Total) {
            ShiftId = l.ShiftId, ShiftName = l.ShiftName, BranchName = l.BranchName, Hours = l.Hours, Occurrences = l.Occurrences,
            PricingMode = l.PricingMode, PeriodFrom = l.PeriodFrom, PeriodTo = l.PeriodTo, LineKey = l.LineKey, Month = l.Month,
            NetAmount = l.NetAmount ?? l.Quantity * l.UnitPriceNet, OverrideReason = l.OverrideReason }).ToArray(),
        Money(i.Total), i.Currency, i.IssueDate, i.DueDate, i.Status, i.PaidAtUtc) {
            CompanyId = i.CompanyId, PeriodFrom = i.PeriodFrom, PeriodTo = i.PeriodTo, Language = i.Language,
            BillingAddress = Address(i.BillingAddress), CustomerBusinessId = i.CustomerBusinessId,
            PaymentReference = i.PaymentReference, IssuerIsSample = i.IssuerIsSample };
}
