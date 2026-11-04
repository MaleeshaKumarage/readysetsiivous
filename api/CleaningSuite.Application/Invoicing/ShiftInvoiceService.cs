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
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using static CleaningSuite.Application.Invoicing.ShiftBillingSettingsService;

namespace CleaningSuite.Application.Invoicing;

public record ShiftInvoiceRequest(Guid CompanyId, IReadOnlyList<Guid> ShiftIds, DateOnly From, DateOnly To,
    Address? BillingAddress = null, string Language = "fi", IReadOnlyList<MonthlyInvoiceOverride>? MonthlyOverrides = null,
    string? PreviewToken = null, bool PartnerMonth = false);
public record ShiftInvoicePreview(Invoice Invoice, string PreviewToken);

public class ShiftInvoiceService(IShiftRepository shifts, IShiftBillingRepository billing, IInvoiceRepository invoices,
    ICompanyRepository companies, IBranchRepository branches, IQualityCycleRepository forms,
    ITenantProfileRepository profiles, ITenantContext tenant, IShiftWorkReservation reservation, TimeProvider clock, IPartnerInvoiceSource? partnerSource = null)
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
        if (request.CompanyId == Guid.Empty || request.ShiftIds is null || (!request.PartnerMonth && request.ShiftIds.Count is < 1 or > 50) || request.ShiftIds.Any(id => id == Guid.Empty))
            Invalid("Select a company and between 1 and 50 shifts.");
        if (request.From.Year < 2000 || request.To.Year > 2100 || request.To < request.From || request.To.DayNumber - request.From.DayNumber >= 366)
            Invalid("Choose an inclusive billing period of at most 366 days between 2000 and 2100.");
        if (request.Language is not ("en" or "fi" or "sv")) Invalid("Choose English, Finnish or Swedish for the invoice.");
        if (request.BillingAddress is { } address && new[] { address.Street, address.PostalCode, address.City, address.Country }.Any(s => s?.Length > 250))
            Invalid("Billing address fields must be at most 250 characters.");
        if (request.PartnerMonth && (request.ShiftIds!.Count != 0 || request.From.Day != 1 || request.To != request.From.AddMonths(1).AddDays(-1)
            || request.MonthlyOverrides?.Count > 0)) Invalid("Partner invoicing requires a full calendar month and no manual shift selection or monthly overrides.");
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
        var partnerRequests = partnerSource is null ? [] : await partnerSource.ListCompanyAsync(request.CompanyId, ct);
        var existing = await invoices.ListAsync(null, null, ct);
        var monthEvidence = new Dictionary<Guid, IReadOnlyList<QualityCycleForm>>();
        if (request.PartnerMonth)
        {
            if (partnerSource is null) Invalid("Partner invoice selection is unavailable.");
            // Completion-month billing covers every company shift, whoever created it. A shift is a candidate
            // when one of its visits was completed in this month; roster and assignment checks run below.
            // A partner-created shift is a candidate only while its request is live and accepted; a cancelled visit is never billed.
            var companyShifts = (await shifts.ListIncludingInactiveAsync(ct)).Where(s => s.CompanyId == request.CompanyId && !existing.Any(i => Billed(i, s.Id))
                && (s.PartnerRequestId is null || partnerRequests.Any(r => r.Id == s.PartnerRequestId && r.LinkedShiftId == s.Id && r.Status != "Cancelled" && r.FirstAcceptedAtUtc.HasValue))).ToArray();
            foreach (var shift in companyShifts)
            {
                var evidence = await forms.ListFormsAsync(shift.Id, null, null, ct);
                if (evidence.Any(f => f.IsSubmitted && !f.ShiftDeletedUtc.HasValue
                    && (f.EndedAtUtc ?? f.SubmittedUtc) is DateTime ended && ended >= from && ended < to)) monthEvidence[shift.Id] = evidence;
            }
            ids = monthEvidence.Keys.Order().ToArray();
        }
        var selected = new List<Shift>();
        foreach (var batch in ids.Chunk(50)) selected.AddRange(await shifts.ListByIdsAsync(batch, ct));
        if (selected.Count != ids.Length || selected.Any(s => s.CompanyId != request.CompanyId)) Invalid("One or more selected shifts are missing or belong to another company. Refresh the selection.");
        if (existing.Any(i => ids.Any(id => Billed(i, id)) && i.PeriodFrom <= request.To && i.PeriodTo >= request.From))
            throw new ShiftConflictException("A selected shift already has an invoice covering these dates. Select an unbilled period or void the existing invoice first.");
        var lines = new List<InvoiceLine>(); var fingerprints = new List<string>();
        foreach (var shift in selected.OrderBy(s => s.Id))
        {
            var pricing = await billing.GetAsync(shift.Id, ct) ?? new ShiftBillingProfile { Id = shift.Id, ShiftId = shift.Id };
            PartnerCleaningRequest? partnerRequest = null;
            if (shift.PartnerRequestId.HasValue)
            {
                partnerRequest = partnerRequests.SingleOrDefault(r => r.Id == shift.PartnerRequestId && r.LinkedShiftId == shift.Id);
                if (partnerRequest is null || partnerRequest.Status == "Cancelled" || !partnerRequest.FirstAcceptedAtUtc.HasValue)
                    Invalid("A partner visit is missing its accepted request or has been cancelled.");
                pricing = new() { Id = shift.Id, ShiftId = shift.Id, CreatedUtc = partnerRequest!.CreatedUtc, UpdatedUtc = partnerRequest.CreatedUtc, Plans = [new("PerOccurrence", partnerRequest.PriceNet, partnerRequest.VatRatePercent, new(2000, 1, 1))] };
            }
            var schedule = ShiftContractSchedule.Snapshot(shift, zone);
            // Completion-month billing must resolve the original visit even when scheduled
            // outside the invoice month. Never select it by its scheduled month.
            // Month billing resolves exactly the visits that have a submitted form, wherever they were scheduled.
            // PostgreSQL timestamps have microsecond precision: a one-tick window
            // can collapse to zero width when its upper bound is persisted/bound.
            var completedStarts = request.PartnerMonth ? monthEvidence[shift.Id].Where(f => f.IsSubmitted && !f.ShiftDeletedUtc.HasValue).Select(f => f.ShiftOccurrenceUtc).ToArray() : [];
            var visitFrom = request.PartnerMonth ? (completedStarts.Length > 0 ? completedStarts.Min() : from) : from;
            var visitTo = request.PartnerMonth ? (completedStarts.Length > 0 ? completedStarts.Max().AddSeconds(1) : visitFrom.AddSeconds(1)) : to;
            var resolved = await QualityCycleOccurrences.ResolvedInWindowAsync(shifts, schedule, visitFrom, visitTo, zone, ct, true);
            if (resolved.Count > 10000) Invalid("Too many scheduled visits in this period. Select a shorter range.");
            var submitted = request.PartnerMonth ? monthEvidence[shift.Id] : await forms.ListFormsAsync(shift.Id, from, to, ct);
            var completionTimes = new Dictionary<DateTime, DateTime>();
            var visits = resolved.Where(v => v.Plan.CompanyId == request.CompanyId
                ).Select(v =>
            {
                var evidence = submitted.Where(f => f.ShiftOccurrenceUtc == v.Occurrence.StartUtc && f.IsSubmitted && !f.ShiftDeletedUtc.HasValue
                    && (!request.PartnerMonth || (f.EndedAtUtc ?? f.SubmittedUtc).HasValue)
                    && (v.EmployeeIds.Count == 0 || v.EmployeeIds.Contains(f.EmployeeId))).ToArray();
                var done = evidence.Select(f => f.EmployeeId).ToHashSet();
                if (request.PartnerMonth && evidence.Length > 0)
                    completionTimes[v.Occurrence.StartUtc] = evidence.Max(f => (f.EndedAtUtc ?? f.SubmittedUtc)!.Value);
                return new BillableShiftVisit(v.Occurrence.StartUtc, v.Occurrence.EndUtc, v.Plan.BranchId, v.Plan.ShiftName,
                    v.EmployeeIds.Count > 0 ? v.EmployeeIds.All(done.Contains) : done.Count > 0);
            }).GroupBy(v => v.StartUtc).Select(g => g.First()).ToArray();
            if (request.PartnerMonth)
            {
                visits = visits.Where(v => v.Completed && completionTimes.TryGetValue(v.StartUtc, out var ended) && ended >= from && ended < to).ToArray();
                if (visits.Length == 0) continue;
            }
            var names = new Dictionary<Guid, string>();
            foreach (var branchId in visits.Select(v => v.BranchId).Concat(shift.QualityCyclePlans.Where(p => p.CompanyId == request.CompanyId).Select(p => p.BranchId)).Append(shift.BranchId).Distinct())
            {
                var branch = await branches.GetAsync(branchId, ct);
                if (branch is null || branch.CompanyId != request.CompanyId) Invalid($"{shift.Name}: an associated branch is missing or belongs to another company.");
                names[branchId] = branch!.Name;
            }
            var calculationFrom = request.PartnerMonth ? DateOnly.FromDateTime(TenantTimeZone.LocalDate(visits.Min(v => v.StartUtc), zone)) : request.From;
            var calculationTo = request.PartnerMonth ? DateOnly.FromDateTime(TenantTimeZone.LocalDate(visits.Max(v => v.StartUtc), zone)) : request.To;
            var calculated = ShiftInvoiceCalculator.Calculate(schedule, pricing, visits, calculationFrom, calculationTo, zone, names, adjustments);
            if (partnerRequest is not null)
            {
                foreach (var line in calculated)
                {
                    if (line.ShiftName is "Partner cleaning" or "Accepted partner cleaning") line.ShiftName = "Cleaning";
                    if (line.Description is "Partner cleaning" or "Accepted partner cleaning") line.Description = "Cleaning";
                }
            }
            if (request.PartnerMonth && visits.Length == 1)
            {
                foreach (var line in calculated)
                {
                    line.CompletedAtUtc = completionTimes[visits[0].StartUtc];
                    line.CompletedOn = DateOnly.FromDateTime(TenantTimeZone.LocalDate(line.CompletedAtUtc.Value, zone));
                }
            }
            lines.AddRange(calculated);
            fingerprints.Add(JsonSerializer.Serialize(new { Shift = shift, Pricing = pricing, PartnerRequest = partnerRequest, Visits = visits,
                Completion = submitted.OrderBy(f => f.Id).Select(f => new { f.Id, f.EmployeeId, f.IsSubmitted, f.ShiftOccurrenceUtc, f.StartedAtUtc, f.EndedAtUtc, f.ShiftDeletedUtc }) }));
        }
        if (lines.Count == 0 && request.PartnerMonth && !string.IsNullOrWhiteSpace(request.PreviewToken))
            throw new ShiftConflictException("Partner work eligibility changed. Load a new preview before issuing.");
        if (lines.Count == 0) Invalid(request.PartnerMonth
            ? "No completed, unbilled visits were found for this month. Choose another month, or select shifts manually under Invoice shifts."
            : "No billable shift work was found in this period. On-call visits must be completed before invoicing.");
        var issueDate = TenantTimeZone.LocalDate(clock.GetUtcNow().UtcDateTime, zone);
        var invoice = new Invoice { Id = Guid.Empty, CreatedUtc = issueDate, UpdatedUtc = issueDate, CompanyId = company.Id,
            PeriodFrom = request.From, PeriodTo = request.To, Language = request.Language, BillingAddress = request.BillingAddress ?? new(),
            CustomerBusinessId = company.BusinessId, Customer = new() { Name = company.Name, Email = company.ContactEmail ?? "", Phone = company.ContactPhone ?? "" },
            Issuer = new() { CompanyName = profile.CompanyName, BusinessId = profile.BusinessId, CompanyAddress = profile.CompanyAddress,
                Email = profile.Email, Phone = profile.Phone, BankAccountIBAN = profile.BankAccountIBAN, BankBic = profile.BankBic },
            IssuerIsSample = profile.InvoiceIssuerIsSample, Lines = lines, Total = new() { Net = lines.Sum(l => l.NetAmount!.Value), Vat = lines.Sum(l => l.VatAmount) },
            Currency = "EUR", IssueDate = issueDate, DueDate = issueDate.AddDays(profile.PaymentTermsDays), Status = Invoice.StatusDraft };
        var tokenPayload = JsonSerializer.Serialize(new { Invoice = invoice, PartnerMonth = request.PartnerMonth, Inputs = adjustments.OrderBy(a => a.ShiftId).ThenBy(a => a.Month), Sources = fingerprints });
        return new(invoice, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenPayload))));
    }
}
