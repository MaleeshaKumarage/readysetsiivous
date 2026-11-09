using CleaningSuite.Application.Common;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Partners;
using FluentValidation;
using FluentValidation.Results;
using Marten;

namespace CleaningSuite.Application.Partners;

public record CreatePartnerKeyRequest(Guid ClientRequestId, Guid BranchId, string Kind, DateOnly Date, string? Notes = null);
public record KeyRequestFeeInput(decimal FeeNet);
public record PartnerKeyRequestDto(Guid Id, int Revision, Guid CompanyId, Guid BranchId, string BranchName,
    string Kind, DateOnly Date, string? Notes, decimal FeeNet, decimal VatRatePercent, string Status, string? CancellationReason);
// The current fee applies to new requests; each listed request keeps the fee it was submitted with.
public record PartnerKeyRequestList(decimal FeeNet, decimal VatRatePercent, IReadOnlyList<PartnerKeyRequestDto> Items);

/// <summary>Key pickup and handover requests. Shares the work reservation so requests serialize with other mutations.</summary>
public sealed class PartnerKeyRequestService(IPartnerStore store, IShiftWorkReservation reservation,
    ITenantProfileRepository profiles, ITenantContext tenant)
{
    private IDocumentSession Db => store.Session;

    public async Task<PartnerKeyRequestList> List(Guid actor, bool staff, Guid? companyId, CancellationToken ct)
    {
        if (staff) await RequireManager(actor, ct); else companyId = (await Partner(actor, ct)).Id;
        var profile = await Profile(ct);
        var query = Db.Query<PartnerKeyRequest>().AsQueryable();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        var items = await query.OrderByDescending(x => x.CreatedUtc).ToListAsync(ct);
        var result = new List<PartnerKeyRequestDto>();
        foreach (var item in items) result.Add(await Map(item, ct));
        return new(profile.KeyRequestFeeNet, profile.DefaultVatRatePercent, result);
    }

    public async Task<PartnerKeyRequestDto> Create(Guid actor, CreatePartnerKeyRequest body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var company = await Partner(actor, ct);
        // Name every missing field at once, as the cleaning request form does.
        var missing = new List<ValidationFailure>();
        var kind = body.Kind?.Trim();
        var notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim();
        if (body.ClientRequestId == Guid.Empty) missing.Add(new("ClientRequestId", "A request identifier is required."));
        if (body.BranchId == Guid.Empty) missing.Add(new("BranchId", "Choose the property for this key request."));
        if (kind is not (PartnerKeyRequest.KindPickup or PartnerKeyRequest.KindHandover)) missing.Add(new("Kind", "Choose key pickup or key handover."));
        if (body.Date == default) missing.Add(new("Date", "Choose the day for the key request."));
        if (notes?.Length > PartnerKeyRequest.MaxNotesLength) missing.Add(new("Notes", $"Keep the notes to {PartnerKeyRequest.MaxNotesLength} characters or fewer."));
        if (missing.Count > 0) throw new ValidationException(missing);

        var id = PartnerKeyRequest.StableId($"key-request:{actor}:{body.ClientRequestId}");
        var existing = await Db.LoadAsync<PartnerKeyRequest>(id, ct);
        if (existing is not null)
        {
            if (existing.CompanyId != company.Id) throw new ShiftConflictException("This request identifier has already been used.");
            return await Map(existing, ct);
        }
        var branch = await Branch(body.BranchId, company.Id, ct);
        var profile = await Profile(ct);
        var item = new PartnerKeyRequest { Id = id, ClientRequestId = body.ClientRequestId, CompanyId = company.Id,
            BranchId = branch.Id, RequestedByEmployeeId = actor, Kind = kind!, Date = body.Date, Notes = notes,
            FeeNet = profile.KeyRequestFeeNet, VatRatePercent = profile.DefaultVatRatePercent };
        Db.Store(item);
        await Db.SaveChangesAsync(ct);
        return await Map(item, ct);
    }

    public async Task<PartnerKeyRequestDto> Cancel(Guid actor, Guid id, int revision, string? reason, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var company = await Partner(actor, ct);
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ValidationException([new("Reason", "Give a reason for the cancellation.")]);
        if (trimmed.Length > PartnerKeyRequest.MaxNotesLength) throw new ValidationException([new("Reason", $"Keep the reason to {PartnerKeyRequest.MaxNotesLength} characters or fewer.")]);
        var item = await Db.LoadAsync<PartnerKeyRequest>(id, ct);
        if (item is null || item.CompanyId != company.Id) throw new NotFoundException("Key request", id);
        if (item.Revision != revision) throw new ShiftConflictException("This request changed. Reload it before making another change.");
        if (item.Status == PartnerKeyRequest.StatusCancelled) return await Map(item, ct);
        item.Status = PartnerKeyRequest.StatusCancelled;
        item.CancellationReason = trimmed;
        item.Revision++;
        Db.Store(item);
        await Db.SaveChangesAsync(ct);
        return await Map(item, ct);
    }

    /// <summary>Staff set the one fee every new key request is charged. Existing requests keep their snapshot.</summary>
    public async Task<PartnerKeyRequestList> SetFee(Guid actor, KeyRequestFeeInput body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct);
        if (body.FeeNet < 0 || body.FeeNet > 10000)
            throw new ValidationException([new("FeeNet", "Enter a fee between 0 and 10000 EUR, excluding VAT.")]);
        var profile = await Profile(ct);
        profile.KeyRequestFeeNet = decimal.Round(body.FeeNet, 2);
        await profiles.SaveAsync(tenant.TenantId, profile, ct);
        return await List(actor, true, null, ct);
    }

    private async Task<Company> Partner(Guid actor, CancellationToken ct)
    {
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person is not { IsActive: true, IdentitySyncPending: false, Role: Employee.RolePartner } || !person.PartnerCompanyId.HasValue)
            throw new UnauthorizedAccessException("An active company partner account is required.");
        var company = await Db.LoadAsync<Company>(person.PartnerCompanyId.Value, ct);
        if (company is not { IsActive: true, IsPartnerEnabled: true })
            throw new UnauthorizedAccessException("Partner access is disabled for this company.");
        return company;
    }

    private async Task RequireManager(Guid actor, CancellationToken ct)
    {
        // Only the authenticated operations controller can supply the legacy, unbound tenant-owner sentinel.
        if (actor == Guid.Empty) return;
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(person.Role))
            throw new UnauthorizedAccessException("An active staff account is required.");
        if (person.Role is not (Employee.RoleAdmin or Employee.RoleManager))
            throw new UnauthorizedAccessException("Administrator or manager access is required.");
    }

    private async Task<Branch> Branch(Guid id, Guid companyId, CancellationToken ct)
    {
        var branch = await Db.LoadAsync<Branch>(id, ct);
        if (branch is null || branch.CompanyId != companyId) throw new NotFoundException("Branch", id);
        if (!branch.IsActive) throw new ShiftConflictException("This property is inactive.");
        return branch;
    }

    private async Task<CleaningSuite.Domain.Tenants.TenantProfile> Profile(CancellationToken ct) =>
        await profiles.GetAsync(tenant.TenantId, ct) ?? throw new NotFoundException("TenantProfile", Guid.Empty);

    private async Task<PartnerKeyRequestDto> Map(PartnerKeyRequest item, CancellationToken ct)
    {
        var branch = await Db.LoadAsync<Branch>(item.BranchId, ct);
        return new(item.Id, item.Revision, item.CompanyId, item.BranchId, branch?.Name ?? "Property", item.Kind,
            item.Date, item.Notes, item.FeeNet, item.VatRatePercent, item.Status, item.CancellationReason);
    }
}
