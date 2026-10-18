using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Invoicing;
using static CleaningSuite.Application.Invoicing.ShiftBillingSettingsService;

namespace CleaningSuite.Application.Invoicing;

public record ShiftInvoiceRequest(Guid CompanyId, IReadOnlyList<Guid> ShiftIds, DateOnly From, DateOnly To,
    Address? BillingAddress = null, string Language = "fi", IReadOnlyList<MonthlyInvoiceOverride>? MonthlyOverrides = null,
    string? PreviewToken = null);
public record ShiftInvoicePreview(Invoice Invoice, string PreviewToken);

public class ShiftInvoiceService(IShiftRepository shifts, IShiftBillingRepository billing, IInvoiceRepository invoices,
    ICompanyRepository companies, IBranchRepository branches, IQualityCycleRepository forms,
    ITenantProfileRepository profiles, ITenantContext tenant, IShiftWorkReservation reservation, TimeProvider clock)
{
    public async Task<ShiftInvoicePreview> PreviewAsync(ShiftInvoiceRequest request, CancellationToken ct) => await BuildAsync(request, ct);

    public async Task<Invoice> IssueAsync(ShiftInvoiceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewToken)) Invalid("Preview the invoice before issuing it.");
        await using var lease = await reservation.AcquireAsync(ct);
        var preview = await BuildAsync(request, ct);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(preview.PreviewToken), Encoding.UTF8.GetBytes(request.PreviewToken!)))
            throw new ShiftConflictException("Invoice inputs, pricing or work records changed. Load a new preview before issuing.");
        var invoice = preview.Invoice;
        var issuer = invoice.Issuer;
        if (string.IsNullOrWhiteSpace(issuer.CompanyName) || string.IsNullOrWhiteSpace(issuer.BusinessId)
            || string.IsNullOrWhiteSpace(issuer.CompanyAddress.Street) || string.IsNullOrWhiteSpace(issuer.CompanyAddress.PostalCode)
            || string.IsNullOrWhiteSpace(issuer.CompanyAddress.City) || !ValidIban(issuer.BankAccountIBAN) || string.IsNullOrWhiteSpace(issuer.BankBic))
            Invalid("Complete invoice issuer name, business ID, address, valid IBAN and BIC in issuer settings before issuing.");
        if (string.IsNullOrWhiteSpace(invoice.BillingAddress.Street) || string.IsNullOrWhiteSpace(invoice.BillingAddress.PostalCode) || string.IsNullOrWhiteSpace(invoice.BillingAddress.City))
            Invalid("Enter the customer's billing street, postal code and city before issuing.");
        var number = await invoices.NextInvoiceNumberForYearAsync(invoice.IssueDate.Year, ct);
        invoice.Id = Guid.NewGuid(); invoice.InvoiceNumber = $"{invoice.IssueDate.Year}-{number:D4}";
        invoice.PaymentReference = PaymentReference($"{invoice.IssueDate.Year}{number:D4}");
        invoice.CreatedUtc = clock.GetUtcNow().UtcDateTime; invoice.UpdatedUtc = invoice.CreatedUtc;
        invoice.Status = Invoice.StatusIssued;
        await invoices.SaveAsync(invoice, ct);
        return invoice;
    }
    public static string PaymentReference(string digits)
    {
        var weights = new[] { 7, 3, 1 }; var total = 0; var offset = 0;
        foreach (var ch in digits.Reverse()) total += (ch - '0') * weights[offset++ % 3];
        return digits + ((10 - total % 10) % 10);
    }
    private async Task<ShiftInvoicePreview> BuildAsync(ShiftInvoiceRequest request, CancellationToken ct)
    {
        if (request.CompanyId == Guid.Empty || request.ShiftIds is null || request.ShiftIds.Count is < 1 or > 50 || request.ShiftIds.Any(id => id == Guid.Empty))
            Invalid("Select a company and between 1 and 50 shifts.");
        if (request.From.Year < 2000 || request.To.Year > 2100 || request.To < request.From || request.To.DayNumber - request.From.DayNumber >= 366)
            Invalid("Choose an inclusive billing period of at most 366 days between 2000 and 2100.");
        if (request.Language is not ("en" or "fi" or "sv")) Invalid("Choose English, Finnish or Swedish for the invoice.");
        if (request.BillingAddress is { } address && new[] { address.Street, address.PostalCode, address.City, address.Country }.Any(s => s?.Length > 250))
            Invalid("Billing address fields must be at most 250 characters.");
        var ids = request.ShiftIds!.Distinct().Order().ToArray();
        var adjustments = request.MonthlyOverrides?.ToArray() ?? [];
        if (adjustments.Length > 600 || adjustments.GroupBy(o => (o.ShiftId, o.Month)).Any(g => g.Count() > 1)) Invalid("Only one override per shift and month is allowed.");
        foreach (var adjustment in adjustments)
        {
            if (!ids.Contains(adjustment.ShiftId) || string.IsNullOrWhiteSpace(adjustment.Reason) || adjustment.Reason.Length > 1000
                || !DateOnly.TryParseExact(adjustment.Month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                Invalid("Monthly overrides require a selected shift, YYYY-MM month and a reason of at most 1,000 characters.");
            Rate(adjustment.AmountNet, 0);
        }
        var company = await companies.GetAsync(request.CompanyId, ct) ?? throw new NotFoundException("Company", request.CompanyId);
        var profile = await profiles.GetAsync(tenant.TenantId, ct) ?? throw new NotFoundException("TenantProfile", Guid.Empty);
        if (profile.PaymentTermsDays is < 0 or > 120) Invalid("Correct invoice payment terms to 0–120 days in issuer settings.");
        var zone = TenantTimeZone.Resolve(profile.TimeZoneId);
        var from = TenantTimeZone.StartOfLocalDay(request.From.ToDateTime(TimeOnly.MinValue), zone);
        var to = TenantTimeZone.StartOfLocalDay(request.To.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        var selected = await shifts.ListByIdsAsync(ids, ct);
        if (selected.Count != ids.Length || selected.Any(s => s.CompanyId != request.CompanyId)) Invalid("One or more selected shifts are missing or belong to another company. Refresh the selection.");
        var existing = await invoices.ListAsync(null, null, ct);
        if (existing.Any(i => ids.Any(id => Billed(i, id)) && i.PeriodFrom <= request.To && i.PeriodTo >= request.From))
            throw new ShiftConflictException("A selected shift already has an invoice covering these dates. Select an unbilled period or void the existing invoice first.");
        var lines = new List<InvoiceLine>(); var fingerprints = new List<string>();
        foreach (var shift in selected.OrderBy(s => s.Id))
        {
            var pricing = await billing.GetAsync(shift.Id, ct) ?? new ShiftBillingProfile { Id = shift.Id, ShiftId = shift.Id };
            var resolved = await QualityCycleOccurrences.ResolvedInWindowAsync(shifts, shift, from, to, zone, ct, true);
            if (resolved.Count > 10000) Invalid("Too many scheduled visits in this period. Select a shorter range.");
            var submitted = await forms.ListFormsAsync(shift.Id, from, to, ct);
            var visits = resolved.Where(v => v.Plan.CompanyId == request.CompanyId).Select(v =>
            {
                var done = submitted.Where(f => f.ShiftOccurrenceUtc == v.Occurrence.StartUtc && f.IsSubmitted && !f.ShiftDeletedUtc.HasValue).Select(f => f.EmployeeId).ToHashSet();
                return new BillableShiftVisit(v.Occurrence.StartUtc, v.Occurrence.EndUtc, v.Plan.BranchId, v.Plan.ShiftName,
                    v.EmployeeIds.Count > 0 ? v.EmployeeIds.All(done.Contains) : done.Count > 0);
            }).GroupBy(v => v.StartUtc).Select(g => g.First()).ToArray();
            var names = new Dictionary<Guid, string>();
            foreach (var branchId in visits.Select(v => v.BranchId).Concat(shift.QualityCyclePlans.Where(p => p.CompanyId == request.CompanyId).Select(p => p.BranchId)).Append(shift.BranchId).Distinct())
            {
                var branch = await branches.GetAsync(branchId, ct);
                if (branch is null || branch.CompanyId != request.CompanyId) Invalid($"{shift.Name}: an associated branch is missing or belongs to another company.");
                names[branchId] = branch!.Name;
            }
            lines.AddRange(ShiftInvoiceCalculator.Calculate(shift, pricing, visits, request.From, request.To, zone, names, adjustments));
            fingerprints.Add(JsonSerializer.Serialize(new { Shift = shift, Pricing = pricing, Visits = visits,
                Completion = submitted.OrderBy(f => f.Id).Select(f => new { f.Id, f.EmployeeId, f.IsSubmitted, f.ShiftOccurrenceUtc, f.StartedAtUtc, f.EndedAtUtc, f.ShiftDeletedUtc }) }));
        }
        if (lines.Count == 0) Invalid("No billable shift work was found in this period. On-call visits must be completed before invoicing.");
        var issueDate = TenantTimeZone.LocalDate(clock.GetUtcNow().UtcDateTime, zone);
        var invoice = new Invoice { Id = Guid.Empty, CreatedUtc = issueDate, UpdatedUtc = issueDate, CompanyId = company.Id,
            PeriodFrom = request.From, PeriodTo = request.To, Language = request.Language, BillingAddress = request.BillingAddress ?? new(),
            CustomerBusinessId = company.BusinessId, Customer = new() { Name = company.Name, Email = company.ContactEmail ?? "", Phone = company.ContactPhone ?? "" },
            Issuer = new() { CompanyName = profile.CompanyName, BusinessId = profile.BusinessId, CompanyAddress = profile.CompanyAddress,
                Email = profile.Email, Phone = profile.Phone, BankAccountIBAN = profile.BankAccountIBAN, BankBic = profile.BankBic },
            IssuerIsSample = profile.InvoiceIssuerIsSample, Lines = lines, Total = new() { Net = lines.Sum(l => l.NetAmount!.Value), Vat = lines.Sum(l => l.VatAmount) },
            Currency = "EUR", IssueDate = issueDate, DueDate = issueDate.AddDays(profile.PaymentTermsDays), Status = Invoice.StatusDraft };
        var tokenPayload = JsonSerializer.Serialize(new { Invoice = invoice, Inputs = adjustments.OrderBy(a => a.ShiftId).ThenBy(a => a.Month), Sources = fingerprints });
        return new(invoice, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenPayload))));
    }
}
