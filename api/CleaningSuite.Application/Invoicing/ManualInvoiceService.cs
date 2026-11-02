using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Invoicing;
using static CleaningSuite.Application.Invoicing.ShiftBillingSettingsService;

namespace CleaningSuite.Application.Invoicing;

public record ManualInvoiceRow(string Description, decimal Quantity, decimal UnitPriceNet);
public record ManualInvoiceRequest(Guid CompanyId, Guid SubmissionId, IReadOnlyList<ManualInvoiceRow>? Lines,
    Address? BillingAddress = null, string Language = "en", string? PreviewToken = null);

/// <summary>Separately agreed charges, using the shared invoice snapshots and numbering.</summary>
public class ManualInvoiceService(IInvoiceRepository invoices, ICompanyRepository companies,
    ITenantProfileRepository profiles, ITenantContext tenant, IShiftWorkReservation reservation, TimeProvider clock)
{
    public Task<ShiftInvoicePreview> PreviewAsync(ManualInvoiceRequest request, CancellationToken ct) =>
        BuildAsync(Normalize(request), ct);

    public async Task<Invoice> IssueAsync(ManualInvoiceRequest request, CancellationToken ct)
    {
        request = Normalize(request);
        if (string.IsNullOrWhiteSpace(request.PreviewToken)) Invalid("Preview the invoice before issuing it.");
        await using var lease = await reservation.AcquireAsync(ct);
        // Retry before reading mutable company/issuer data, including after payment or voiding.
        var existing = await invoices.GetByManualSubmissionIdAsync(request.SubmissionId, ct);
        if (existing is not null)
        {
            if (existing.ManualRequestFingerprint != Fingerprint(request))
                throw new ShiftConflictException("This submission was already issued with different rows or customer details. Start a new invoice.");
            return existing;
        }
        var preview = await BuildAsync(request, ct);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(preview.PreviewToken), Encoding.UTF8.GetBytes(request.PreviewToken!)))
            throw new ShiftConflictException("Invoice details changed. Load a new preview before issuing.");
        var invoice = preview.Invoice;
        var issuer = invoice.Issuer;
        if (string.IsNullOrWhiteSpace(issuer.CompanyName) || string.IsNullOrWhiteSpace(issuer.BusinessId)
            || string.IsNullOrWhiteSpace(issuer.CompanyAddress.Street) || string.IsNullOrWhiteSpace(issuer.CompanyAddress.PostalCode)
            || string.IsNullOrWhiteSpace(issuer.CompanyAddress.City) || !ValidIban(issuer.BankAccountIBAN) || string.IsNullOrWhiteSpace(issuer.BankBic))
            Invalid("Complete invoice issuer name, business ID, address, valid IBAN and BIC in issuer settings before issuing.");
        var number = await invoices.NextInvoiceNumberForYearAsync(invoice.IssueDate.Year, ct);
        invoice.Id = Guid.NewGuid();
        invoice.InvoiceNumber = $"{invoice.IssueDate.Year}-{number:D4}";
        invoice.PaymentReference = ShiftInvoiceService.PaymentReference($"{invoice.IssueDate.Year}{number:D4}");
        invoice.CreatedUtc = clock.GetUtcNow().UtcDateTime; invoice.UpdatedUtc = invoice.CreatedUtc;
        invoice.Status = Invoice.StatusIssued;
        await invoices.SaveAsync(invoice, ct);
        return invoice;
    }

    private async Task<ShiftInvoicePreview> BuildAsync(ManualInvoiceRequest request, CancellationToken ct)
    {
        var company = await companies.GetAsync(request.CompanyId, ct) ?? throw new NotFoundException("Company", request.CompanyId);
        var profile = await profiles.GetAsync(tenant.TenantId, ct) ?? throw new NotFoundException("TenantProfile", Guid.Empty);
        Rate(0, profile.DefaultVatRatePercent);
        if (profile.PaymentTermsDays is < 0 or > 120) Invalid("Correct invoice payment terms to 0–120 days in issuer settings.");
        var lines = request.Lines!.Select((row, index) =>
        {
            var net = Round(row.Quantity * row.UnitPriceNet);
            return new InvoiceLine { LineKey = $"manual-{index + 1}", Description = row.Description,
                Quantity = row.Quantity, UnitPriceNet = row.UnitPriceNet, NetAmount = net,
                VatRatePercent = profile.DefaultVatRatePercent, VatAmount = Round(net * profile.DefaultVatRatePercent / 100m) };
        }).ToList();
        var total = new Money { Net = lines.Sum(l => l.NetAmount!.Value), Vat = lines.Sum(l => l.VatAmount) };
        if (total.Gross <= 0) Invalid("The invoice total must be greater than zero.");
        var issueDate = TenantTimeZone.LocalDate(clock.GetUtcNow().UtcDateTime, TenantTimeZone.Resolve(profile.TimeZoneId));
        var invoice = new Invoice { Id = Guid.Empty, CreatedUtc = issueDate, UpdatedUtc = issueDate,
            CompanyId = company.Id, CustomerBusinessId = company.BusinessId,
            Customer = new() { Name = company.Name, Email = company.ContactEmail ?? "", Phone = company.ContactPhone ?? "" },
            BillingAddress = CopyAddress(request.BillingAddress!), Language = request.Language,
            Issuer = new() { CompanyName = profile.CompanyName, BusinessId = profile.BusinessId,
                CompanyAddress = CopyAddress(profile.CompanyAddress), Email = profile.Email, Phone = profile.Phone,
                BankAccountIBAN = profile.BankAccountIBAN, BankBic = profile.BankBic },
            IssuerIsSample = profile.InvoiceIssuerIsSample, Currency = "EUR", Lines = lines, Total = total,
            IssueDate = issueDate, DueDate = issueDate.AddDays(profile.PaymentTermsDays), Status = Invoice.StatusDraft,
            ManualSubmissionId = request.SubmissionId, ManualRequestFingerprint = Fingerprint(request) };
        return new(invoice, Hash(invoice));
    }

    private static ManualInvoiceRequest Normalize(ManualInvoiceRequest request)
    {
        if (request.CompanyId == Guid.Empty || request.SubmissionId == Guid.Empty) Invalid("Select a company and provide a valid invoice submission ID.");
        if (request.Language is not ("en" or "fi" or "sv")) Invalid("Choose English, Finnish or Swedish for the invoice.");
        if (request.Lines is null || request.Lines.Count is < 1 or > 100) Invalid("Enter between 1 and 100 invoice rows.");
        if (request.BillingAddress is null || new[] { request.BillingAddress.Street, request.BillingAddress.PostalCode,
            request.BillingAddress.City, request.BillingAddress.Country }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 250))
            Invalid("Enter the full customer billing address, with at most 250 characters per field.");
        var lines = request.Lines!.Select(row =>
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Description) || row.Description.Length > 1000)
                Invalid("Each invoice row needs a description of at most 1,000 characters.");
            if (row!.Quantity <= 0 || row.Quantity > 10_000 || row.Quantity != Round(row.Quantity))
                Invalid("Enter a quantity greater than zero and at most 10,000, with at most two decimals.");
            Rate(row.UnitPriceNet, 0);
            return row with { Description = row.Description.Trim(), Quantity = Canonical(row.Quantity), UnitPriceNet = Canonical(row.UnitPriceNet) };
        }).ToArray();
        var address = request.BillingAddress!;
        return request with { Lines = lines, BillingAddress = new() { Street = address.Street.Trim(), PostalCode = address.PostalCode.Trim(),
            City = address.City.Trim(), Country = address.Country!.Trim() } };
    }

    private static decimal Canonical(decimal value) => decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    private static Address CopyAddress(Address address) => new() { Street = address.Street, PostalCode = address.PostalCode, City = address.City, Country = address.Country };
    private static string Fingerprint(ManualInvoiceRequest request) => Hash(new { request.CompanyId, request.Language, request.BillingAddress, request.Lines });
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
