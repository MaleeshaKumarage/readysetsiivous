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
    string? PreviewToken = null, bool PartnerMonth = false, IReadOnlyList<UnstartedVisitDecision>? UnstartedDecisions = null);
/// <summary>Tick choice for a scheduled visit that has not started. Excluded visits are not billed (or counted as done) on the invoice.</summary>
public record UnstartedVisitDecision(Guid ShiftId, DateTime StartUtc, bool Include);
public record UnstartedVisit(Guid ShiftId, string ShiftName, DateTime StartUtc, DateTime EndUtc, bool Included);
public record ShiftInvoicePreview(Invoice Invoice, string PreviewToken, IReadOnlyList<UnstartedVisit>? UnstartedVisits = null);

public class ShiftInvoiceService(IShiftRepository shifts, IShiftBillingRepository billing, IInvoiceRepository invoices,
    ICompanyRepository companies, IBranchRepository branches, IQualityCycleRepository forms,
    ITenantProfileRepository profiles, ITenantContext tenant, IShiftWorkReservation reservation, TimeProvider clock, IPartnerInvoiceSource? partnerSource = null)
{
    private static DateTime PartnerAnchor(PartnerCleaningRequest request, TimeZoneInfo zone) =>
        request.PlannedStartUtc ?? TenantTimeZone.StartOfLocalDay(request.FromDate.ToDateTime(TimeOnly.MinValue), zone);

    public async Task<ShiftInvoicePreview> PreviewAsync(ShiftInvoiceRequest request, CancellationToken ct) => await BuildAsync(request, ct);

    public async Task<Invoice> IssueAsync(ShiftInvoiceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewToken)) Invalid("Preview the invoice before issuing it.");
        await using var lease = await reservation.AcquireAsync(ct);
        var preview = await BuildAsync(request, ct);
        var invoice = preview.Invoice;
        var issuer = invoice.Issuer;
        if (string.IsNullOrWhiteSpace(issuer.CompanyName) || string.IsNullOrWhiteSpace(issuer.BusinessId)
            || string.IsNullOrWhiteSpace(issuer.CompanyAddress.Street) || string.IsNullOrWhiteSpace(issuer.CompanyAddress.PostalCode)
            || string.IsNullOrWhiteSpace(issuer.CompanyAddress.City) || !ValidIban(issuer.BankAccountIBAN) || string.IsNullOrWhiteSpace(issuer.BankBic))
            Invalid("Complete invoice issuer name, business ID, address, valid IBAN and BIC in issuer settings before issuing.");
        if (string.IsNullOrWhiteSpace(invoice.BillingAddress.Street) || string.IsNullOrWhiteSpace(invoice.BillingAddress.PostalCode) || string.IsNullOrWhiteSpace(invoice.BillingAddress.City))
            Invalid("Enter the customer's billing street, postal code and city before issuing.");
        // Field checks run on the freshly built preview first, so a missing field is named rather than reported as a stale preview.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(preview.PreviewToken), Encoding.UTF8.GetBytes(request.PreviewToken!)))
            throw new ShiftConflictException("Invoice inputs, pricing or work records changed. Load a new preview before issuing.");
        // A preview with only unticked unstarted visits has no lines; it can be reviewed but never issued empty.
        if (invoice.Lines.Count == 0) Invalid("No billable work is selected. Tick at least one unstarted visit or choose billable work before issuing.");
        if (invoice.Total.Gross <= 0) Invalid("The invoice total must be greater than zero. Check the billable visits, prices and VAT before issuing.");
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
        if (request.To < request.From) Invalid("The billing period must end on or after its start date.");
        if (request.From.Year < 2000 || request.To.Year > 2100 || request.To.DayNumber - request.From.DayNumber >= 366)
            Invalid("Choose an inclusive billing period of at most 366 days between 2000 and 2100.");
        if (request.Language is not ("en" or "fi" or "sv")) Invalid("Choose English, Finnish or Swedish for the invoice.");
        if (request.BillingAddress is { } address && new[] { address.Street, address.PostalCode, address.City, address.Country }.Any(s => s?.Length > 250))
            Invalid("Billing address fields must be at most 250 characters.");
        if (request.PartnerMonth && (request.ShiftIds!.Count != 0 || request.From.Day != 1 || request.To != request.From.AddMonths(1).AddDays(-1)
            || request.MonthlyOverrides?.Count > 0)) Invalid("Partner invoicing requires a full calendar month and no manual shift selection or monthly overrides.");
        var decisions = request.UnstartedDecisions?.ToArray() ?? [];
        if (request.PartnerMonth && decisions.Length > 0) Invalid("Partner invoices do not use unstarted visit choices.");
        if (decisions.GroupBy(d => (d.ShiftId, d.StartUtc)).Any(g => g.Count() > 1)) Invalid("Each unstarted visit can be chosen only once.");
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
            if (partnerSource is null) Invalid("Partner invoice selection is unavailable right now. Reload the page and try again.");
            // Completion-month billing covers every company shift, whoever created it. A shift is a candidate
            // when one of its visits was completed in this month; roster and assignment checks run below.
            // A partner-created shift is a candidate only while its request is live and accepted; a cancelled visit is never billed.
            var companyShifts = (await shifts.ListIncludingInactiveAsync(ct)).Where(s => s.CompanyId == request.CompanyId && !existing.Any(i => Billed(i, s.Id))
                && (s.PartnerRequestId is null || partnerRequests.Any(r => r.Id == s.PartnerRequestId && r.LinkedShiftId == s.Id && r.Status != "Cancelled" && r.FirstAcceptedAtUtc.HasValue))).ToArray();
            foreach (var shift in companyShifts)
            {
                var evidence = await forms.ListFormsAsync(shift.Id, null, null, ct);
                // A partner visit is anchored to its request's planned start, so a moved visit is not billed from its old form.
                var partner = shift.PartnerRequestId is { } partnerId ? partnerRequests.FirstOrDefault(r => r.Id == partnerId) : null;
                var anchor = partner is null ? (DateTime?)null : PartnerAnchor(partner, zone);
                // Only forms completed inside the invoice month can select visits; earlier completions never seed the window.
                var inMonth = evidence.Where(f => f.IsSubmitted && !f.ShiftDeletedUtc.HasValue
                    && (f.EndedAtUtc ?? f.SubmittedUtc) is DateTime ended && ended >= from && ended < to).ToArray();
                if (inMonth.Any(f => anchor is null || f.ShiftOccurrenceUtc == anchor.Value)) monthEvidence[shift.Id] = inMonth;
            }
            ids = monthEvidence.Keys.Order().ToArray();
        }
        var selected = new List<Shift>();
        foreach (var batch in ids.Chunk(50)) selected.AddRange(await shifts.ListByIdsAsync(batch, ct));
        if (selected.Count != ids.Length || selected.Any(s => s.CompanyId != request.CompanyId)) Invalid("One or more selected shifts are missing or belong to another company. Refresh the selection.");
        if (existing.Any(i => ids.Any(id => Billed(i, id)) && i.PeriodFrom <= request.To && i.PeriodTo >= request.From))
            throw new ShiftConflictException("A selected shift already has an invoice covering these dates. Select an unbilled period or void the existing invoice first.");
        var lines = new List<InvoiceLine>(); var fingerprints = new List<string>(); var unstarted = new List<UnstartedVisit>();
        foreach (var shift in selected.OrderBy(s => s.Id))
        {
            var pricing = await billing.GetAsync(shift.Id, ct) ?? new ShiftBillingProfile { Id = shift.Id, ShiftId = shift.Id };
            PartnerCleaningRequest? partnerRequest = null;
            if (shift.PartnerRequestId.HasValue)
            {
                partnerRequest = partnerRequests.SingleOrDefault(r => r.Id == shift.PartnerRequestId && r.LinkedShiftId == shift.Id);
                if (partnerRequest is null || partnerRequest.Status == "Cancelled" || !partnerRequest.FirstAcceptedAtUtc.HasValue)
                    Invalid("A partner visit has no accepted request or was cancelled. Refresh the selection to continue.");
                pricing = new() { Id = shift.Id, ShiftId = shift.Id, CreatedUtc = partnerRequest!.CreatedUtc, UpdatedUtc = partnerRequest.CreatedUtc, Plans = [new("PerOccurrence", partnerRequest.PriceNet, partnerRequest.VatRatePercent, new(2000, 1, 1))] };
            }
            // A shift billed at a branch price reads that version from the branch: one price, never a copy on the shift.
            if (partnerRequest is null && pricing.BranchPriceEffectiveFrom is { } branchFrom)
            {
                var branchForPrice = await branches.GetAsync(shift.BranchId, ct);
                var version = branchForPrice?.Versions.FirstOrDefault(v => v.EffectiveFrom == branchFrom);
                if (version is null) ShiftBillingSettingsService.Invalid($"{shift.Name}: the branch price from {branchFrom:yyyy-MM-dd} is missing. Restore the branch price history before invoicing.");
                pricing = new() { Id = shift.Id, ShiftId = shift.Id, CreatedUtc = pricing.CreatedUtc, UpdatedUtc = pricing.UpdatedUtc,
                    BranchPriceEffectiveFrom = branchFrom, Overrides = pricing.Overrides,
                    Plans = [new(version!.Mode, version.PriceNet, version.VatRatePercent, new(2000, 1, 1))] };
            }
            var schedule = ShiftContractSchedule.Snapshot(shift, zone);
            // Completion-month billing must resolve the original visit even when scheduled
            // outside the invoice month. Never select it by its scheduled month.
            // Month billing resolves exactly the visits that have a submitted form, wherever they were scheduled.
            // PostgreSQL timestamps have microsecond precision: a one-tick window
            // can collapse to zero width when its upper bound is persisted/bound.
            var completedStarts = request.PartnerMonth
                ? (partnerRequest is not null ? new[] { PartnerAnchor(partnerRequest, zone) }
                    : monthEvidence[shift.Id].Where(f => f.IsSubmitted && !f.ShiftDeletedUtc.HasValue).Select(f => f.ShiftOccurrenceUtc).ToArray())
                : Array.Empty<DateTime>();
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
                var completed = v.EmployeeIds.Count > 0 ? v.EmployeeIds.All(done.Contains) : done.Count > 0;
                // A visit with no started or submitted form is unstarted: billed only when the user ticks it in.
                var started = submitted.Any(f => f.ShiftOccurrenceUtc == v.Occurrence.StartUtc && !f.ShiftDeletedUtc.HasValue && (f.StartedAtUtc.HasValue || f.IsSubmitted));
                var isUnstarted = !request.PartnerMonth && !started;
                var decision = decisions.FirstOrDefault(d => d.ShiftId == shift.Id && d.StartUtc == v.Occurrence.StartUtc);
                if (isUnstarted && !unstarted.Any(u => u.ShiftId == shift.Id && u.StartUtc == v.Occurrence.StartUtc))
                    unstarted.Add(new(shift.Id, shift.Name, v.Occurrence.StartUtc, v.Occurrence.EndUtc, decision?.Include == true));
                return new BillableShiftVisit(v.Occurrence.StartUtc, v.Occurrence.EndUtc, v.Plan.BranchId, v.Plan.ShiftName,
                    completed || (isUnstarted && decision?.Include == true), isUnstarted);
            }).GroupBy(v => v.StartUtc).Select(g => g.First()).ToArray();
            if (request.PartnerMonth)
            {
                visits = visits.Where(v => v.Completed && completionTimes.TryGetValue(v.StartUtc, out var ended) && ended >= from && ended < to).ToArray();
                if (visits.Length == 0) continue;
            }
            // A monthly fee is divided by every scheduled visit in the calendar month, not just the invoiced window,
            // so two partial invoices for one month bill the same total as one full-month invoice.
            var monthVisits = Array.Empty<BillableShiftVisit>();
            if (!request.PartnerMonth && pricing.Plans.Any(p => p.Mode == "Monthly"))
            {
                var monthStart = TenantTimeZone.StartOfLocalDay(new DateTime(request.From.Year, request.From.Month, 1), zone);
                var monthEnd = TenantTimeZone.StartOfLocalDay(new DateOnly(request.To.Year, request.To.Month, 1).AddMonths(1).ToDateTime(TimeOnly.MinValue), zone);
                var monthResolved = await QualityCycleOccurrences.ResolvedInWindowAsync(shifts, schedule, monthStart, monthEnd, zone, ct, true);
                monthVisits = monthResolved.Where(v => v.Plan.CompanyId == request.CompanyId)
                    .Select(v => new BillableShiftVisit(v.Occurrence.StartUtc, v.Occurrence.EndUtc, v.Plan.BranchId, v.Plan.ShiftName, false))
                    .GroupBy(v => v.StartUtc).Select(g => g.First()).ToArray();
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
            // Monthly prices are charged over the requested month, not over the span of completed visits, so a full
            // month is never prorated as if it were partial. Visit dates still come from the visits themselves.
            var billingPeriod = request.PartnerMonth ? (request.From, request.To) : (ValueTuple<DateOnly, DateOnly>?)null;
            var calculated = ShiftInvoiceCalculator.Calculate(schedule, pricing, visits, calculationFrom, calculationTo, zone, names, adjustments,
                request.PartnerMonth ? completionTimes : null, billingPeriod, monthVisits);
            if (partnerRequest is not null)
            {
                foreach (var line in calculated)
                {
                    if (line.ShiftName is "Partner cleaning" or "Accepted partner cleaning") line.ShiftName = "Cleaning";
                    if (line.Description is "Partner cleaning" or "Accepted partner cleaning") line.Description = "Cleaning";
                }
            }
            lines.AddRange(calculated);
            fingerprints.Add(JsonSerializer.Serialize(new { Shift = shift, Pricing = pricing, PartnerRequest = partnerRequest, Visits = visits,
                Completion = submitted.OrderBy(f => f.Id).Select(f => new { f.Id, f.EmployeeId, f.IsSubmitted, f.ShiftOccurrenceUtc, f.StartedAtUtc, f.EndedAtUtc, f.ShiftDeletedUtc }) }));
        }
        foreach (var decision in decisions)
            if (!unstarted.Any(u => u.ShiftId == decision.ShiftId && u.StartUtc == decision.StartUtc))
                Invalid("The unstarted visits changed. Refresh the preview and choose again.");
        var unstartedVisits = unstarted.OrderBy(u => u.ShiftId).ThenBy(u => u.StartUtc).ToArray();
        if (lines.Count == 0 && request.PartnerMonth && !string.IsNullOrWhiteSpace(request.PreviewToken))
            throw new ShiftConflictException("Partner work eligibility changed. Load a new preview before issuing.");
        // Unstarted-only selections still return a preview, so the tick list can be shown and used.
        if (lines.Count == 0 && unstartedVisits.Length == 0) Invalid(request.PartnerMonth
            ? "No completed, unbilled visits were found for this month. Choose another month, or select shifts manually under Invoice shifts."
            : "No billable work was found for the selected shifts in this period. Completed visits and scheduled hours or fees are billed. Unstarted visits are billed only when ticked in the unstarted visits list.");
        var issueDate = TenantTimeZone.LocalDate(clock.GetUtcNow().UtcDateTime, zone);
        var invoice = new Invoice { Id = Guid.Empty, CreatedUtc = issueDate, UpdatedUtc = issueDate, CompanyId = company.Id,
            PeriodFrom = request.From, PeriodTo = request.To, Language = request.Language, BillingAddress = request.BillingAddress ?? new(),
            CustomerBusinessId = company.BusinessId, Customer = new() { Name = company.Name, Email = company.ContactEmail ?? "", Phone = company.ContactPhone ?? "" },
            Issuer = new() { CompanyName = profile.CompanyName, BusinessId = profile.BusinessId, CompanyAddress = profile.CompanyAddress,
                Email = profile.Email, Phone = profile.Phone, BankAccountIBAN = profile.BankAccountIBAN, BankBic = profile.BankBic },
            IssuerIsSample = profile.InvoiceIssuerIsSample, Lines = lines, Total = new() { Net = lines.Sum(l => l.NetAmount!.Value), Vat = lines.Sum(l => l.VatAmount) },
            Currency = "EUR", IssueDate = issueDate, DueDate = issueDate.AddDays(profile.PaymentTermsDays), Status = Invoice.StatusDraft };
        var tokenPayload = JsonSerializer.Serialize(new { Invoice = invoice, PartnerMonth = request.PartnerMonth, Inputs = adjustments.OrderBy(a => a.ShiftId).ThenBy(a => a.Month), Unstarted = unstartedVisits, Sources = fingerprints });
        return new(invoice, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenPayload))), unstartedVisits);
    }
}
