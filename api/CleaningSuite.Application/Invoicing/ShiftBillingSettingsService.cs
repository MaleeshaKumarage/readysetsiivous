using CleaningSuite.Application.Common;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using FluentValidation.Results;

namespace CleaningSuite.Application.Invoicing;

public record InvoiceIssuerSettings(string CompanyName, string BusinessId, Address CompanyAddress,
    string Email, string Phone, string BankAccountIBAN, string BankBic, int PaymentTermsDays, decimal DefaultVatRatePercent, bool IsSampleData = false);
public record SelectBranchPriceRequest(DateOnly EffectiveFrom);
public record BillingSettingsResponse(IReadOnlyList<ShiftPricingPlan> Plans, IReadOnlyList<ShiftOccurrencePrice> Overrides,
    IReadOnlyList<BranchPriceVersion>? BranchPriceVersions = null, DateOnly? BranchPriceEffectiveFrom = null);

public class ShiftBillingSettingsService(IShiftRepository shifts, IShiftBillingRepository billing,
    IInvoiceRepository invoices, ITenantProfileRepository profiles, ITenantContext tenant, IShiftWorkReservation reservation,
    IBranchRepository? branches = null)
{
    public static void Invalid(string message) => throw new ValidationException([new ValidationFailure("Billing", message)]);
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    public static void Rate(decimal amount, decimal vat)
    {
        if (amount < 0 || amount > 1_000_000 || amount != Round(amount)) Invalid("Enter a net EUR rate from 0 to 1,000,000 with at most two decimals.");
        if (vat < 0 || vat > 50 || vat != Round(vat)) Invalid("Enter a VAT percentage from 0 to 50 with at most two decimals.");
    }
    public async Task<BillingSettingsResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var shift = await shifts.GetAsync(id, ct) ?? throw new NotFoundException("Shift", id);
        var profile = await billing.GetAsync(id, ct);
        var branch = branches is null ? null : await branches.GetAsync(shift.BranchId, ct);
        return new(profile?.Plans.OrderBy(p => p.EffectiveFrom).ToArray() ?? [], profile?.Overrides.OrderBy(p => p.OccurrenceStartUtc).ToArray() ?? [],
            branch?.Versions.ToArray(), profile?.BranchPriceEffectiveFrom);
    }
    public async Task<BillingSettingsResponse> SaveAsync(Guid id, ShiftPricingPlan plan, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var shift = await shifts.GetAsync(id, ct) ?? throw new NotFoundException("Shift", id);
        if (shift.IsBranchMain) throw new ShiftConflictException("This is the property's main visit shift. Each visit uses the property price in force on its date. Change the property price instead.");
        if (shift.PartnerRequestId.HasValue) throw new ShiftConflictException("Partner visit pricing is frozen when requested. Update branch pricing for future requests instead.");
        if (plan.Mode is not ("Monthly" or "Hourly" or "PerOccurrence")) Invalid("Choose monthly, hourly or per-occurrence pricing.");
        if (plan.Mode == "PerOccurrence" && !shift.Schedule.IsVisitPriced) Invalid("Per-occurrence pricing is for on-call and one-time shifts.");
        Rate(plan.RateNet, plan.VatRatePercent);
        if (plan.EffectiveFrom.Year is < 2000 or > 2100) Invalid("Enter a valid effective date between 2000 and 2100.");
        if ((await invoices.ListAsync(null, null, ct)).Any(i => Billed(i, id) && i.PeriodTo >= plan.EffectiveFrom))
            throw new ShiftConflictException("This price would change an invoiced period. Start after the last invoiced date, or void the affected invoice first.");
        var profile = await billing.GetAsync(id, ct) ?? new() { Id = id, ShiftId = id };
        if (profile.BranchPriceEffectiveFrom is not null)
            throw new ShiftConflictException("This shift is priced from its branch. Choose a branch price version instead of entering a separate price.");
        profile.Plans.RemoveAll(p => p.EffectiveFrom == plan.EffectiveFrom);
        profile.Plans.Add(plan);
        profile.Plans = profile.Plans.OrderBy(p => p.EffectiveFrom).ToList();
        profile.UpdatedUtc = DateTime.UtcNow;
        await billing.SaveAsync(profile, ct);
        return new(profile.Plans, profile.Overrides);
    }
    /// <summary>Picks which branch price version a shift is billed at. Allowed until the shift has an issued invoice.</summary>
    public async Task<BillingSettingsResponse> SelectBranchPriceAsync(Guid id, DateOnly effectiveFrom, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var shift = await shifts.GetAsync(id, ct) ?? throw new NotFoundException("Shift", id);
        if (shift.IsBranchMain) throw new ShiftConflictException("This is the property's main visit shift. Each visit uses the property price in force on its date. Change the property price instead.");
        if (shift.PartnerRequestId.HasValue) throw new ShiftConflictException("Partner visit pricing is frozen when requested. Update branch pricing for future requests instead.");
        if (branches is null) Invalid("Branch prices are unavailable right now. Reload the page and try again.");
        var branch = await branches!.GetAsync(shift.BranchId, ct) ?? throw new NotFoundException("Branch", shift.BranchId);
        if (!branch.Versions.Any(v => v.EffectiveFrom == effectiveFrom)) Invalid("Choose a price version from this shift's branch.");
        if ((await invoices.ListAsync(null, null, ct)).Any(i => Billed(i, id)))
            throw new ShiftConflictException("This shift already has an issued invoice, so its price cannot change. Void the invoice first.");
        var profile = await billing.GetAsync(id, ct) ?? new() { Id = id, ShiftId = id };
        profile.BranchPriceEffectiveFrom = effectiveFrom; profile.Plans = []; profile.UpdatedUtc = DateTime.UtcNow;
        await billing.SaveAsync(profile, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// The branch price version a new shift is billed at. One version needs no choice; with history the version in force
    /// today is the default; an explicit date must name one of the branch's versions. Null when the branch has no price.
    /// </summary>
    public static BranchPriceVersion? ChooseBranchPrice(Branch branch, DateOnly? requested, DateOnly today)
    {
        var versions = branch.Versions;
        if (versions.Count == 0)
        {
            if (requested is not null) Invalid("This property has no cleaning price yet. Set a price before choosing one for the shift.");
            return null;
        }
        if (requested is { } date)
            return versions.FirstOrDefault(v => v.EffectiveFrom == date)
                ?? throw new ValidationException([new ValidationFailure("Billing", "Choose a price version from this property.")]);
        return branch.PriceOn(today) ?? versions[0];
    }

    public async Task SaveOccurrenceAsync(Guid id, ShiftOccurrencePrice price, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var shift = await shifts.GetAsync(id, ct) ?? throw new NotFoundException("Shift", id);
        if (shift.PartnerRequestId.HasValue) throw new ShiftConflictException("Partner visit pricing is frozen when requested. Update branch pricing for future requests instead.");
        if (!shift.Schedule.IsVisitPriced) Invalid("Visit price overrides are for on-call and one-time shifts.");
        Rate(price.RateNet, 0);
        if (price.OccurrenceStartUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(price.Reason) || price.Reason.Length > 1000)
            Invalid("Provide a UTC visit start and an override reason of at most 1,000 characters.");
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var date = DateOnly.FromDateTime(TenantTimeZone.LocalDate(price.OccurrenceStartUtc, zone));
        if ((await invoices.ListAsync(null, null, ct)).Any(i => Billed(i, id) && i.PeriodFrom <= date && i.PeriodTo >= date))
            throw new ShiftConflictException("This visit has already been invoiced. Void the invoice before changing its price.");
        // Every visit of a main shift is a stored extra visit, so it is looked up directly.
        var mainVisit = await BranchMainVisits.FindAsync(shifts, shift, price.OccurrenceStartUtc, ct);
        if (shift.IsBranchMain)
        {
            if (mainVisit is null) Invalid("The selected visit no longer exists or was cancelled. Reload occurrences.");
            // A partner booking keeps the price frozen on its request.
            if (mainVisit!.PartnerRequestId is not null)
                throw new ShiftConflictException("Partner visit pricing is frozen when requested. Update branch pricing for future requests instead.");
        }
        else
        {
            var visits = await CleaningSuite.Application.QualityCycle.QualityCycleOccurrences.ResolvedInWindowAsync(shifts, ShiftContractSchedule.Snapshot(shift, zone),
                price.OccurrenceStartUtc, price.OccurrenceStartUtc.AddTicks(1), zone, ct, true);
            if (!visits.Any(v => v.Occurrence.StartUtc == price.OccurrenceStartUtc)) Invalid("The selected visit no longer exists or was cancelled. Reload occurrences.");
        }
        var profile = await billing.GetAsync(id, ct) ?? new() { Id = id, ShiftId = id };
        if (shift.IsBranchMain)
        {
            // A main shift's visits are priced from the property.
            var versions = branches is null ? [] : (await branches.GetAsync(shift.BranchId, ct))?.Versions ?? [];
            if ((versions.LastOrDefault(v => v.EffectiveFrom <= date) ?? versions.FirstOrDefault())?.Mode != "PerOccurrence")
                Invalid("Set a per-visit price on the property before adding a visit price override.");
        }
        else if (profile.Plans.Where(p => p.EffectiveFrom <= date).OrderByDescending(p => p.EffectiveFrom).FirstOrDefault()?.Mode != "PerOccurrence")
            Invalid("Configure per-completed-visit pricing effective on this visit's date before adding a visit price override.");
        profile.Overrides.RemoveAll(p => p.OccurrenceStartUtc == price.OccurrenceStartUtc);
        profile.Overrides.Add(price with { Reason = price.Reason.Trim() });
        profile.UpdatedUtc = DateTime.UtcNow;
        await billing.SaveAsync(profile, ct);
    }
    internal static bool Billed(Invoice invoice, Guid shiftId) => invoice.Status is Invoice.StatusIssued or Invoice.StatusPaid
        && invoice.PeriodFrom.HasValue && invoice.PeriodTo.HasValue && invoice.Lines.Any(l => l.ShiftId == shiftId);

    public async Task<InvoiceIssuerSettings> GetIssuerAsync(CancellationToken ct)
    {
        var p = await profiles.GetAsync(tenant.TenantId, ct) ?? throw new NotFoundException("TenantProfile", Guid.Empty);
        return new(p.CompanyName, p.BusinessId, p.CompanyAddress, p.Email, p.Phone, p.BankAccountIBAN, p.BankBic, p.PaymentTermsDays, p.DefaultVatRatePercent, p.InvoiceIssuerIsSample);
    }
    public async Task<InvoiceIssuerSettings> SaveIssuerAsync(InvoiceIssuerSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.CompanyName) || settings.CompanyName.Length > 200 || settings.BusinessId?.Length > 50
            || settings.PaymentTermsDays is < 0 or > 120 || settings.CompanyAddress is null) Invalid("Enter issuer name, address and payment terms from 0 to 120 days.");
        Rate(0, settings.DefaultVatRatePercent);
        if (new[] { settings.Email, settings.Phone, settings.BankAccountIBAN, settings.BankBic, settings.CompanyAddress!.Street, settings.CompanyAddress.PostalCode,
            settings.CompanyAddress.City, settings.CompanyAddress.Country }.Any(s => s?.Length > 250)) Invalid("Issuer fields must be at most 250 characters.");
        if (!string.IsNullOrWhiteSpace(settings.Email) && !System.Net.Mail.MailAddress.TryCreate(settings.Email, out _)) Invalid("Enter a valid issuer email address.");
        if (!string.IsNullOrWhiteSpace(settings.BankAccountIBAN) && !ValidIban(settings.BankAccountIBAN)) Invalid("Enter a valid IBAN, including its check digits.");
        if (!string.IsNullOrWhiteSpace(settings.BankBic) && !System.Text.RegularExpressions.Regex.IsMatch(settings.BankBic.Trim().ToUpperInvariant(), "^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$")) Invalid("Enter a valid 8- or 11-character BIC.");
        await using var lease = await reservation.AcquireAsync(ct);
        var p = await profiles.GetAsync(tenant.TenantId, ct) ?? throw new NotFoundException("TenantProfile", Guid.Empty);
        p.CompanyName = settings.CompanyName.Trim(); p.BusinessId = settings.BusinessId?.Trim() ?? "";
        p.CompanyAddress = settings.CompanyAddress!; p.Email = settings.Email?.Trim() ?? ""; p.Phone = settings.Phone?.Trim() ?? "";
        p.BankAccountIBAN = settings.BankAccountIBAN?.Replace(" ", "").ToUpperInvariant() ?? "";
        p.BankBic = settings.BankBic?.Trim().ToUpperInvariant() ?? ""; p.PaymentTermsDays = settings.PaymentTermsDays;
        p.DefaultVatRatePercent = settings.DefaultVatRatePercent; p.InvoiceIssuerIsSample = settings.IsSampleData; p.UpdatedUtc = DateTime.UtcNow;
        await profiles.SaveAsync(tenant.TenantId, p, ct);
        return await GetIssuerAsync(ct);
    }
    internal static bool ValidIban(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var iban = value.Replace(" ", "").ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(iban, "^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$")) return false;
        var reordered = iban[4..] + iban[..4]; var remainder = 0;
        foreach (var ch in reordered)
            foreach (var digit in (char.IsAsciiLetterUpper(ch) ? (ch - 'A' + 10).ToString() : ch.ToString())) remainder = (remainder * 10 + digit - '0') % 97;
        return remainder == 1;
    }
}
