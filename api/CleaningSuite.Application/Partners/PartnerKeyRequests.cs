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
public record KeyRequestAssignment(int Revision, Guid EmployeeId);
public record KeyRequestRevision(int Revision);
public record KeyRequestPhotoDto(Guid Id, int SizeBytes, DateTime UploadedUtc);
// The assignee and assignee name are staff-only: partner responses leave them null. Photos are shared with the partner.
public record PartnerKeyRequestDto(Guid Id, int Revision, Guid CompanyId, Guid BranchId, string BranchName,
    string Kind, DateOnly Date, string? Notes, decimal FeeNet, decimal VatRatePercent, string Status, string? CancellationReason,
    DateTime? CompletedUtc = null, Guid? AssignedEmployeeId = null, string? AssignedEmployeeName = null,
    IReadOnlyList<KeyRequestPhotoDto>? Photos = null);
// The current fee applies to new requests; each listed request keeps the fee it was submitted with.
public record PartnerKeyRequestList(decimal FeeNet, decimal VatRatePercent, IReadOnlyList<PartnerKeyRequestDto> Items);
public sealed record KeyRequestPhotoFile(byte[] Content, string ContentType);

/// <summary>Stores key request photo bytes outside the document.</summary>
public interface IKeyRequestFileStore
{
    Task SaveAsync(Guid requestId, KeyRequestPhoto photo, byte[] content, CancellationToken ct);
    Task<byte[]?> ReadAsync(Guid requestId, KeyRequestPhoto photo, CancellationToken ct);
    Task DeleteAsync(Guid requestId, KeyRequestPhoto photo);
}

/// <summary>Key pickup and handover requests. Shares the work reservation so requests serialize with other mutations.</summary>
public sealed class PartnerKeyRequestService(IPartnerStore store, IShiftWorkReservation reservation,
    ITenantProfileRepository profiles, ITenantContext tenant, IKeyRequestFileStore files, IUploadContentValidator uploads, TimeProvider clock)
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
        foreach (var item in items) result.Add(await Map(item, staff, ct));
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
            return await Map(existing, false, ct);
        }
        var branch = await Branch(body.BranchId, company.Id, ct);
        var profile = await Profile(ct);
        var item = new PartnerKeyRequest { Id = id, ClientRequestId = body.ClientRequestId, CompanyId = company.Id,
            BranchId = branch.Id, RequestedByEmployeeId = actor, Kind = kind!, Date = body.Date, Notes = notes,
            FeeNet = profile.KeyRequestFeeNet, VatRatePercent = profile.DefaultVatRatePercent };
        Db.Store(item);
        await Db.SaveChangesAsync(ct);
        return await Map(item, false, ct);
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
        CheckRevision(item, revision);
        if (item.Status == PartnerKeyRequest.StatusCancelled) return await Map(item, false, ct);
        if (item.Status == PartnerKeyRequest.StatusCompleted) throw new ShiftConflictException("Completed key requests cannot be cancelled.");
        item.Status = PartnerKeyRequest.StatusCancelled;
        item.CancellationReason = trimmed;
        item.Revision++;
        Db.Store(item);
        await Db.SaveChangesAsync(ct);
        return await Map(item, false, ct);
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

    /// <summary>Staff choose who collects or hands over the keys. Allowed until the request is completed or cancelled.</summary>
    public async Task<PartnerKeyRequestDto> Assign(Guid actor, Guid id, KeyRequestAssignment body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct);
        var item = await Require(id, ct);
        CheckRevision(item, body.Revision);
        EnsureOpen(item);
        var employee = await Db.LoadAsync<Employee>(body.EmployeeId, ct);
        if (employee is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(employee.Role))
            throw new ValidationException([new("EmployeeId", "Choose an active staff member.")]);
        item.AssignedEmployeeId = employee.Id;
        item.Revision++;
        Db.Store(item);
        await Db.SaveChangesAsync(ct);
        return await Map(item, true, ct);
    }

    /// <summary>Marks the keys collected or handed over. Needs an assigned employee; photos can be added afterwards.</summary>
    public async Task<PartnerKeyRequestDto> Complete(Guid actor, Guid id, KeyRequestRevision body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var item = await Require(id, ct);
        await RequireManagerOrAssignee(actor, item, ct);
        CheckRevision(item, body.Revision);
        if (item.Status == PartnerKeyRequest.StatusCompleted) return await Map(item, true, ct);
        EnsureOpen(item);
        if (!item.AssignedEmployeeId.HasValue) throw new ShiftConflictException("Assign an employee before completing this key request.");
        item.Status = PartnerKeyRequest.StatusCompleted;
        item.CompletedUtc = clock.GetUtcNow().UtcDateTime;
        item.Revision++;
        Db.Store(item);
        await Db.SaveChangesAsync(ct);
        return await Map(item, true, ct);
    }

    /// <summary>Adds one photo to a completed key request. The bytes are validated before anything is stored.</summary>
    public async Task<PartnerKeyRequestDto> AddPhoto(Guid actor, Guid id, byte[] content, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var item = await Require(id, ct);
        await RequireManagerOrAssignee(actor, item, ct);
        // Photos can be taken from the moment someone is assigned, during the visit, and stay addable after completion.
        if (item.Status == PartnerKeyRequest.StatusCancelled) throw new ShiftConflictException("Photos cannot be added to a cancelled key request.");
        if (!item.AssignedEmployeeId.HasValue) throw new ShiftConflictException("Assign an employee before adding photos.");
        if (item.Photos.Count >= PartnerKeyRequest.MaxPhotos) throw new ShiftConflictException($"A key request can have at most {PartnerKeyRequest.MaxPhotos} photos.");
        var extension = uploads.ValidateKeyRequestPhoto(content);
        var photo = new KeyRequestPhoto { Id = Guid.NewGuid(), Extension = extension, SizeBytes = content.Length, UploadedUtc = clock.GetUtcNow().UtcDateTime };
        await files.SaveAsync(item.Id, photo, content, ct);
        try
        {
            item.Photos.Add(photo);
            // A new photo changes what the partner and staff see, so stale clients must refresh before acting again.
            item.Revision++;
            Db.Store(item);
            await Db.SaveChangesAsync(ct);
        }
        catch
        {
            await files.DeleteAsync(item.Id, photo);
            throw;
        }
        return await Map(item, true, ct);
    }

    /// <summary>Photo bytes for a manager or the assigned employee.</summary>
    public async Task<KeyRequestPhotoFile> Photo(Guid actor, Guid id, Guid photoId, CancellationToken ct)
    {
        var item = await Require(id, ct);
        await RequireManagerOrAssignee(actor, item, ct);
        return await ReadPhoto(item, photoId, ct);
    }

    /// <summary>Photo bytes for the partner that owns the request. Other companies get not-found.</summary>
    public async Task<KeyRequestPhotoFile> PartnerPhoto(Guid actor, Guid id, Guid photoId, CancellationToken ct)
    {
        var company = await Partner(actor, ct);
        var item = await Db.LoadAsync<PartnerKeyRequest>(id, ct);
        if (item is null || item.CompanyId != company.Id) throw new NotFoundException("Key request", id);
        return await ReadPhoto(item, photoId, ct);
    }

    /// <summary>Open and completed requests assigned to the calling staff member.</summary>
    public async Task<IReadOnlyList<PartnerKeyRequestDto>> Assigned(Guid actor, CancellationToken ct)
    {
        await RequireStaff(actor, ct);
        var items = await Db.Query<PartnerKeyRequest>().Where(x => x.AssignedEmployeeId == actor && x.Status != PartnerKeyRequest.StatusCancelled)
            .OrderBy(x => x.Date).ToListAsync(ct);
        var result = new List<PartnerKeyRequestDto>();
        foreach (var item in items) result.Add(await Map(item, true, ct));
        return result;
    }

    private async Task<KeyRequestPhotoFile> ReadPhoto(PartnerKeyRequest item, Guid photoId, CancellationToken ct)
    {
        var photo = item.Photos.FirstOrDefault(x => x.Id == photoId) ?? throw new NotFoundException("Key request photo", photoId);
        var bytes = await files.ReadAsync(item.Id, photo, ct) ?? throw new NotFoundException("Key request photo", photoId);
        return new(bytes, photo.Extension switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" });
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

    private async Task RequireStaff(Guid actor, CancellationToken ct)
    {
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(person.Role))
            throw new UnauthorizedAccessException("An active staff account is required.");
    }

    // Completing a request and adding its photos: managers and administrators, or the employee it is assigned to.
    private async Task RequireManagerOrAssignee(Guid actor, PartnerKeyRequest item, CancellationToken ct)
    {
        // Every change needs a real, active staff account: the tenant owner must have an employee record too.
        if (actor == Guid.Empty) throw new UnauthorizedAccessException("An active staff account is required.");
        await RequireStaff(actor, ct);
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person!.Role is Employee.RoleAdmin or Employee.RoleManager) return;
        if (item.AssignedEmployeeId != actor) throw new UnauthorizedAccessException("Only the assigned employee or a manager can change this key request.");
    }

    private async Task RequireManager(Guid actor, CancellationToken ct)
    {
        if (actor == Guid.Empty) throw new UnauthorizedAccessException("An active staff account is required.");
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(person.Role))
            throw new UnauthorizedAccessException("An active staff account is required.");
        if (person.Role is not (Employee.RoleAdmin or Employee.RoleManager))
            throw new UnauthorizedAccessException("Administrator or manager access is required.");
    }

    private async Task<PartnerKeyRequest> Require(Guid id, CancellationToken ct) =>
        await Db.LoadAsync<PartnerKeyRequest>(id, ct) ?? throw new NotFoundException("Key request", id);

    private static void CheckRevision(PartnerKeyRequest item, int revision)
    {
        if (item.Revision != revision) throw new ShiftConflictException("This request changed. Reload it before making another change.");
    }

    private static void EnsureOpen(PartnerKeyRequest item)
    {
        if (item.Status is PartnerKeyRequest.StatusCompleted or PartnerKeyRequest.StatusCancelled)
            throw new ShiftConflictException("This key request is already finished.");
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

    private async Task<PartnerKeyRequestDto> Map(PartnerKeyRequest item, bool staff, CancellationToken ct)
    {
        var branch = await Db.LoadAsync<Branch>(item.BranchId, ct);
        string? assigneeName = null;
        if (staff && item.AssignedEmployeeId.HasValue)
        {
            var employee = await Db.LoadAsync<Employee>(item.AssignedEmployeeId.Value, ct);
            assigneeName = employee is null ? null : $"{employee.FirstName} {employee.LastName}".Trim();
        }
        return new(item.Id, item.Revision, item.CompanyId, item.BranchId, branch?.Name ?? "Property", item.Kind,
            item.Date, item.Notes, item.FeeNet, item.VatRatePercent, item.Status, item.CancellationReason,
            item.CompletedUtc, staff ? item.AssignedEmployeeId : null, assigneeName,
            item.Photos.Select(p => new KeyRequestPhotoDto(p.Id, p.SizeBytes, p.UploadedUtc)).ToList());
    }
}
