using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Invoicing;

/// <summary>Invoice with full snapshots. PDF is regenerable from this document alone.</summary>
public class Invoice : BaseDocument
{
    public const string StatusDraft = "Draft";
    public const string StatusIssued = "Issued";
    public const string StatusPaid = "Paid";
    public const string StatusVoided = "Voided";

    public string InvoiceNumber { get; set; } = "";
    public Guid BookingId { get; set; }
    public string BookingNumber { get; set; } = "";
    public Customer Customer { get; set; } = new();

    /// <summary>Copy of TenantProfile at issue time.</summary>
    public IssuerInfo Issuer { get; set; } = new();

    public List<InvoiceLine> Lines { get; set; } = [];
    public Money Total { get; set; } = new();
    public string Currency { get; set; } = "EUR";
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = StatusIssued;
    public DateTime? PaidAtUtc { get; set; }
}

public class IssuerInfo
{
    public string CompanyName { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public Address CompanyAddress { get; set; } = new();
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string BankAccountIBAN { get; set; } = "";
    public string BankBic { get; set; } = "";
}

public class InvoiceLine
{
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPriceNet { get; set; }
    public decimal VatRatePercent { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total => Quantity * UnitPriceNet + VatAmount;
}
