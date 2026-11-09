using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Inventory;
using FluentValidation;
using Marten;

namespace CleaningSuite.Application.Inventory;

public record InventoryAcknowledgmentInput(Guid BatchId, Guid CompanyId, IReadOnlyList<Guid>? UsageIds, string? Message);
public record InventoryAcknowledgmentResponse(Guid Version, string Status, string? Note);
public record InventoryPartnerOption(Guid Id, string Name);
public record InventoryAcknowledgmentDto(Guid Id, Guid BatchId, Guid CompanyId, string CompanyName,
    string Message, string ItemName, string LocationName, int Quantity, string Unit, string Note,
    DateTime TakenUtc, IReadOnlyList<Guid> PhotoIds, string Status, string ResponseNote,
    string? RespondedByName, DateTime? RespondedUtc, DateTime CreatedUtc, Guid Version);
public record InventoryAcknowledgmentPage(IReadOnlyList<InventoryAcknowledgmentDto> Items, int PendingCount, bool HasMore);

/// <summary>Company-scoped disclosure and acknowledgment; inventory takes are never changed by a response.</summary>
public sealed class InventoryAcknowledgmentService(IPartnerStore store, IShiftWorkReservation reservation,
    InventoryService inventory, TimeProvider clock)
{
    private IDocumentSession Db => store.Session;

    private async Task<Employee> Partner(Guid actor, CancellationToken ct)
    {
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person is not { IsActive: true, IdentitySyncPending: false, Role: Employee.RolePartner, PartnerCompanyId: not null })
            throw new UnauthorizedAccessException("An active company partner account is required.");
        var company = await Db.LoadAsync<Company>(person.PartnerCompanyId.Value, ct);
        if (company is not { IsActive: true, IsPartnerEnabled: true })
            throw new UnauthorizedAccessException("Partner access is disabled for this company.");
        return person;
    }

    public async Task<IReadOnlyList<InventoryPartnerOption>> Partners(string? search, CancellationToken ct)
    {
        var query = Db.Query<Company>().Where(x => x.IsActive && x.IsPartnerEnabled);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        return (await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(25).ToListAsync(ct))
            .Select(x => new InventoryPartnerOption(x.Id, x.Name)).ToArray();
    }

    public async Task<IReadOnlyList<InventoryAcknowledgmentDto>> Create(string subject, InventoryAcknowledgmentInput body, CancellationToken ct)
    {
        if (body.BatchId == Guid.Empty || body.CompanyId == Guid.Empty || body.UsageIds is not { Count: > 0 and <= 50 }
            || body.UsageIds.Any(x => x == Guid.Empty) || body.UsageIds.Distinct().Count() != body.UsageIds.Count)
            throw new ValidationException("Choose a partner and between 1 and 50 distinct inventory entries.");
        var message = CleanNote(body.Message);
        await using var lease = await reservation.AcquireAsync(ct);
        var company = await Db.LoadAsync<Company>(body.CompanyId, ct);
        if (company is not { IsActive: true, IsPartnerEnabled: true })
            throw new ValidationException("Choose an active partner company.");
        var existing = await Db.Query<InventoryAcknowledgment>().Where(x => x.BatchId == body.BatchId).ToListAsync(ct);
        if (existing.Count > 0)
        {
            if (existing.Count == body.UsageIds.Count && existing.All(x => x.CompanyId == body.CompanyId
                && x.Message == message && body.UsageIds.Contains(x.Id))) return existing.Select(Map).ToArray();
            throw new InventoryConflictException("This request identifier has already been used. Refresh before sending again.");
        }
        var rows = new List<InventoryAcknowledgment>();
        foreach (var id in body.UsageIds)
        {
            if (await Db.LoadAsync<InventoryAcknowledgment>(id, ct) is not null)
                throw new InventoryConflictException("An acknowledgment has already been requested for a selected entry. Refresh the log.");
            var usage = await Db.LoadAsync<InventoryUsage>(id, ct) ?? throw new NotFoundException("Inventory entry", id);
            if (usage.PhotosPurgedUtc.HasValue || usage.Photos.Count == 0 || usage.TakenUtc < InventoryService.PhotoRetentionCutoff(clock.GetUtcNow().UtcDateTime))
                throw new ValidationException("A selected entry's photos have expired. Choose entries with available photos.");
            var now = clock.GetUtcNow().UtcDateTime;
            var row = InventoryAcknowledgment.ForUsage(id);
            row.BatchId = body.BatchId; row.CompanyId = company.Id; row.CompanyName = company.Name;
            row.RequestedBySubject = subject; row.Message = message; row.ItemName = usage.ItemName;
            row.LocationName = usage.LocationName; row.Quantity = usage.Quantity; row.Unit = usage.Unit;
            row.Note = usage.Note; row.TakenUtc = usage.TakenUtc; row.PhotoIds = usage.Photos.Select(x => x.Id).ToList();
            row.CreatedUtc = now; row.UpdatedUtc = now;
            rows.Add(row);
        }
        // All rows are validated before any insert; the batch and portal notification are one atomic commit.
        foreach (var row in rows) Db.Insert(row);
        await Db.SaveChangesAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<InventoryAcknowledgmentDto>> Lookup(IReadOnlyList<Guid> usageIds, CancellationToken ct)
    {
        if (usageIds.Count > 200) throw new ValidationException("Choose up to 200 entries.");
        var ids = usageIds.ToArray();
        return (await Db.Query<InventoryAcknowledgment>().Where(x => ids.Contains(x.Id)).ToListAsync(ct)).Select(Map).ToArray();
    }

    public async Task<InventoryAcknowledgmentPage> List(Guid actor, bool history, int skip, CancellationToken ct)
    {
        var person = await Partner(actor, ct);
        var query = Db.Query<InventoryAcknowledgment>().Where(x => x.CompanyId == person.PartnerCompanyId!.Value);
        var count = await query.CountAsync(x => x.Status == "Pending", ct);
        var filtered = history ? query.Where(x => x.Status != "Pending") : query.Where(x => x.Status == "Pending");
        var rows = await filtered.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Skip(Math.Max(0, skip)).Take(26).ToListAsync(ct);
        return new(rows.Take(25).Select(Map).ToArray(), count, rows.Count > 25);
    }

    public async Task<InventoryAcknowledgmentDto> Respond(Guid actor, Guid id, InventoryAcknowledgmentResponse body, CancellationToken ct)
    {
        var note = CleanNote(body.Note);
        if (body.Status is not ("Acknowledged" or "IssueReported")) throw new ValidationException("Choose acknowledgment or report an issue.");
        if (body.Status == "IssueReported" && note.Length == 0) throw new ValidationException("Describe the issue before sending.");
        await using var lease = await reservation.AcquireAsync(ct);
        var person = await Partner(actor, ct);
        var row = await Owned(person, id, ct);
        // A lost response can be retried safely without replacing the original actor or timestamp.
        if (row.Status == body.Status && row.ResponseNote == note && row.RespondedByEmployeeId == actor) return Map(row);
        if (row.Version != body.Version || row.Status != "Pending") throw new InventoryConflictException("This request has already changed. Refresh to see the response.");
        row.Status = body.Status; row.ResponseNote = note; row.RespondedByEmployeeId = actor;
        row.RespondedByName = $"{person.FirstName} {person.LastName}".Trim();
        row.RespondedUtc = row.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        Db.Store(row); await Db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<(byte[] Bytes, string ContentType)> Photo(Guid actor, Guid id, Guid photoId, CancellationToken ct)
    {
        var row = await Owned(await Partner(actor, ct), id, ct);
        if (!row.PhotoIds.Contains(photoId)) throw new NotFoundException("Inventory photo", photoId);
        return await inventory.ReadPhotoAsync(row.Id, photoId, null, ct);
    }

    private async Task<InventoryAcknowledgment> Owned(Employee person, Guid id, CancellationToken ct)
    {
        var row = await Db.LoadAsync<InventoryAcknowledgment>(id, ct);
        if (row is null || row.CompanyId != person.PartnerCompanyId) throw new NotFoundException("Acknowledgment request", id);
        return row;
    }

    private static string CleanNote(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length > 500) throw new ValidationException("Message must be 500 characters or fewer.");
        return text;
    }

    private static InventoryAcknowledgmentDto Map(InventoryAcknowledgment x) => new(x.Id, x.BatchId, x.CompanyId,
        x.CompanyName, x.Message, x.ItemName, x.LocationName, x.Quantity, x.Unit, x.Note, x.TakenUtc,
        x.PhotoIds, x.Status, x.ResponseNote, x.RespondedByName, x.RespondedUtc, x.CreatedUtc, x.Version);
}
