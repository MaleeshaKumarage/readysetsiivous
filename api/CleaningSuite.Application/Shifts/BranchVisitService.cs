using System.Globalization;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using FluentValidation.Results;
using Marten;

namespace CleaningSuite.Application.Shifts;

/// <summary>Adds one visit to a branch's main shift. Dates and times are local to the tenant.</summary>
public record AddBranchVisit(Guid BranchId, DateOnly FromDate, DateOnly ToDate, string StartTime, string EndTime,
    string? Notes = null, Guid? EmployeeId = null, bool UseDefaultEmployee = true, string? CleaningType = null);
public record SetVisitEmployee(Guid? EmployeeId, int OfferRevision);
public record SetVisitCleaningType(string CleaningType);
public record SetDefaultEmployee(Guid? EmployeeId);

/// <summary>A visit of a branch main shift, as staff see it. It carries no price: the price comes from the property.</summary>
public record BranchVisitDto(Guid Id, Guid ShiftId, Guid BranchId, int Number, string Source, DateTime StartUtc, DateTime EndUtc,
    DateOnly FromDate, DateOnly ToDate, string? Notes, Guid? EmployeeId, Guid? OfferedEmployeeId, int OfferRevision,
    Guid? PartnerRequestId, string Status, string CleaningType, string TimeZoneId);

public record BranchMainDto(Guid ShiftId, Guid BranchId, string Name, bool IsActive, Guid? DefaultEmployeeId, IReadOnlyList<BranchVisitDto> Visits);

/// <summary>
/// Visits that staff add to a branch's main shift, and the offer each one makes to a cleaner. A visit is offered to
/// the chosen cleaner, or to the shift's default cleaner. A cleaner who accepts work automatically gets it at once;
/// anyone else must accept the offer before the visit is theirs. Every change is one commit under the tenant work reservation.
/// </summary>
public class BranchVisitService(IPartnerStore store, IShiftWorkReservation reservation, IShiftRepository shifts,
    ITenantProfileRepository profiles, ITenantContext tenant, IPushOutbox? push = null)
{
    public const string StatusUnassigned = "Unassigned";
    public const string StatusOffered = "Offered";
    public const string StatusAssigned = "Assigned";
    private const string AddedReason = "Added from the admin panel";
    private IDocumentSession Db => store.Session;

    public async Task<BranchVisitDto> Add(AddBranchVisit body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var failures = new List<ValidationFailure>();
        if (body.BranchId == Guid.Empty) failures.Add(new("BranchId", "Choose the property for this visit."));
        if (body.FromDate == default) failures.Add(new("FromDate", "Choose the first day of the visit."));
        if (body.ToDate == default) failures.Add(new("ToDate", "Choose the last day of the visit."));
        if (!TimeOnly.TryParseExact(body.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
            failures.Add(new("StartTime", "Enter the start time as HH:mm."));
        if (!TimeOnly.TryParseExact(body.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
            failures.Add(new("EndTime", "Enter the end time as HH:mm."));
        if (body.Notes?.Length > 4000) failures.Add(new("Notes", "Use at most 4000 characters for the visit notes."));
        if (body.CleaningType is not null && !CleaningTypes.IsKnown(body.CleaningType)) failures.Add(new("CleaningType", "Cleaning type must be Deep or Normal."));
        if (failures.Count > 0) throw new ValidationException(failures);
        if (body.FromDate.Year < 2000 || body.ToDate.Year > 2099 || body.ToDate < body.FromDate || body.ToDate.DayNumber - body.FromDate.DayNumber > 366)
            throw new ValidationException([new ValidationFailure("ToDate", "Choose a last day on or after the first day, at most 366 days later.")]);
        var start = PartnerService.LocalToUtc(body.FromDate.ToDateTime(startTime), zone);
        var end = PartnerService.LocalToUtc(body.ToDate.ToDateTime(endTime), zone);
        if (end <= start) throw new ValidationException([new ValidationFailure("EndTime", "The end time must be after the start time.")]);

        var branch = await Db.LoadAsync<Branch>(body.BranchId, ct) ?? throw new NotFoundException("Branch", body.BranchId);
        if (!branch.IsActive) throw new ShiftConflictException("This property is inactive.");
        var company = await Db.LoadAsync<Company>(branch.CompanyId, ct) ?? throw new NotFoundException("Company", branch.CompanyId);
        if (!company.IsActive) throw new ShiftConflictException("This company is inactive.");
        if (branch.Versions.Count == 0)
            throw new ShiftConflictException("This property has no cleaning price yet. Set the property price before adding a visit, so the visit can be invoiced.");

        var shift = await BranchMainShifts.FindOrCreateAsync(Db, branch, TenantTimeZone.LocalDate(start, zone), ct);
        var others = await Db.Query<ShiftOccurrenceException>().Where(x => x.ShiftId == shift.Id).ToListAsync(ct);
        // A colliding start moves the visit without shortening it.
        var freeStart = BranchMainShifts.FreeStart(start, end, others);
        end = end.Add(freeStart - start); start = freeStart;
        var visit = ShiftOccurrenceException.Create(shift.Id, null, start, end, ShiftOccurrenceExceptionStatus.Extra, AddedReason);
        visit.Id = Guid.NewGuid();
        visit.Sequence = shift.NextOccurrenceSequence(); visit.Source = ShiftOccurrenceException.SourceAdminPanel;
        visit.Notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim();
        visit.CleaningType = body.CleaningType ?? CleaningTypes.Normal;
        var employeeId = body.EmployeeId ?? (body.UseDefaultEmployee ? shift.DefaultEmployeeId : null);
        if (employeeId.HasValue) await OfferTo(shift, visit, employeeId.Value, zone, ct);
        await ShiftTabs.StoreAsync(Db, shift, ct); Db.Store(visit);
        await Db.SaveChangesAsync(ct);
        return Map(visit, shift, zone, zoneId);
    }

    /// <summary>The branch's main shift and its visits in a window, or null when the branch has no main shift yet.</summary>
    public async Task<BranchMainDto?> Get(Guid branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var shift = await Db.LoadAsync<Shift>(Shift.BranchMainId(branchId), ct);
        if (shift is null) return null;
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var visits = (await Db.Query<ShiftOccurrenceException>().Where(x => x.ShiftId == shift.Id).ToListAsync(ct))
            .Where(x => x.Status == ShiftOccurrenceExceptionStatus.Extra && x.EndUtc > fromUtc && x.StartUtc < toUtc)
            .OrderBy(x => x.StartUtc).Select(x => Map(x, shift, zone, zoneId)).ToList();
        return new(shift.Id, shift.BranchId, shift.Name, shift.IsActive, shift.DefaultEmployeeId, visits);
    }

    /// <summary>Sets the cleaner that new visits of a main shift are offered to. Visits already added are not changed.</summary>
    public async Task<BranchMainDto> SetDefaultEmployee(Guid shiftId, SetDefaultEmployee body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var shift = await Main(shiftId, ct);
        if (body.EmployeeId is { } id) await Worker(id, ct);
        shift.DefaultEmployeeId = body.EmployeeId; shift.UpdatedUtc = DateTime.UtcNow;
        Db.Store(shift); await Db.SaveChangesAsync(ct);
        return new(shift.Id, shift.BranchId, shift.Name, shift.IsActive, shift.DefaultEmployeeId, []);
    }

    /// <summary>
    /// Changes who a visit is for. A waiting offer is withdrawn and the new cleaner is offered the visit.
    /// A cleaner who had accepted is released, until work has started. Null leaves the visit unassigned.
    /// </summary>
    public async Task<BranchVisitDto> SetEmployee(Guid shiftId, Guid visitId, SetVisitEmployee body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var shift = await Main(shiftId, ct);
        var visit = await Visit(shift, visitId, ct);
        PartnerShiftGuard.EnsureOrdinary(visit);
        if (visit.OfferRevision != body.OfferRevision) throw new ShiftConflictException("This visit changed. Reload the visits and try again.");
        if (!shift.IsActive) throw new ShiftConflictException("Activate the shift before assigning visits.");
        await EnsureUnstarted(shift.Id, visit.StartUtc, ct);
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var released = visit.EmployeeId;
        visit.EmployeeId = null; visit.OfferedEmployeeId = null; visit.OfferId = null; visit.OfferRevision++;
        // A roster change made in the occurrence list would otherwise still decide who the visit is for.
        Db.Delete<ShiftOccurrenceAssignment>(ShiftOccurrenceAssignment.Identity(shift.Id, null, visit.Id));
        if (body.EmployeeId is { } next) await OfferTo(shift, visit, next, zone, ct);
        visit.UpdatedUtc = DateTime.UtcNow; Db.Store(visit);
        if (push is not null && released.HasValue && released != visit.EmployeeId)
            await push.StageScheduleChangeAsync(Db, shift.Id, [released.Value], DateTimeOffset.UtcNow, ct);
        await Db.SaveChangesAsync(ct);
        return Map(visit, shift, zone, zoneId);
    }

    /// <summary>
    /// Sets whether a visit is a Deep or Normal clean. Any visit can change, including after it has been cleaned;
    /// the price does not change with it.
    /// </summary>
    public async Task<BranchVisitDto> SetCleaningType(Guid shiftId, Guid visitId, SetVisitCleaningType body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var shift = await Main(shiftId, ct);
        var visit = await Visit(shift, visitId, ct);
        if (!CleaningTypes.IsKnown(body.CleaningType))
            throw new ValidationException([new ValidationFailure("CleaningType", "Cleaning type must be Deep or Normal.")]);
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        visit.CleaningType = body.CleaningType; visit.UpdatedUtc = DateTime.UtcNow; Db.Store(visit);
        // A partner visit keeps its booking in step, so accepting the booking again does not undo this choice.
        if (visit.PartnerRequestId is { } requestId && await Db.LoadAsync<PartnerCleaningRequest>(requestId, ct) is { } booking)
        {
            booking.CleaningType = body.CleaningType; booking.UpdatedUtc = DateTime.UtcNow; Db.Store(booking);
        }
        await Db.SaveChangesAsync(ct);
        return Map(visit, shift, zone, zoneId);
    }

    /// <summary>Visit offers waiting for this cleaner's answer, in the shape cleaners already see offers in.</summary>
    public async Task<IReadOnlyList<PartnerCleanerOfferDto>> Offers(Guid actor, CancellationToken ct)
    {
        var waiting = await Db.Query<ShiftOccurrenceException>().Where(x => x.OfferedEmployeeId == actor).ToListAsync(ct);
        if (waiting.Count == 0) return [];
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var result = new List<PartnerCleanerOfferDto>();
        foreach (var visit in waiting.Where(x => x.OfferId.HasValue).OrderBy(x => x.StartUtc))
        {
            var shift = await Db.LoadAsync<Shift>(visit.ShiftId, ct);
            if (shift is not { IsActive: true, IsBranchMain: true }) continue;
            var branch = await Db.LoadAsync<Branch>(shift.BranchId, ct);
            var company = await Db.LoadAsync<Company>(shift.CompanyId, ct);
            if (branch is not { IsActive: true } || company is not { IsActive: true }) continue;
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(visit.StartUtc, zone);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(visit.EndUtc.AddTicks(-1), zone);
            result.Add(new(visit.OfferId!.Value, visit.Id, visit.OfferRevision, branch.Name, branch.Street, branch.PostalCode, branch.City,
                DateOnly.FromDateTime(localStart), DateOnly.FromDateTime(localEnd), localStart.ToString("HH:mm", CultureInfo.InvariantCulture),
                visit.StartUtc, (int)Math.Min(1440, Math.Round((visit.EndUtc - visit.StartUtc).TotalMinutes)), visit.Notes ?? shift.Notes, zoneId, visit.CleaningType ?? "Normal"));
        }
        return result;
    }

    /// <summary>Accepts a visit offer. Returns null when the id is not a visit offer for this cleaner, so the caller can try a partner offer.</summary>
    public async Task<BranchVisitDto?> Accept(Guid actor, Guid offerId, int version, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var visit = await Db.Query<ShiftOccurrenceException>().FirstOrDefaultAsync(x => x.OfferId == offerId && x.OfferedEmployeeId == actor, ct);
        if (visit is null) return null;
        await Worker(actor, ct);
        if (visit.OfferRevision != version) throw new ShiftConflictException("This offer changed. Reload your offers and try again.");
        var shift = await Db.LoadAsync<Shift>(visit.ShiftId, ct);
        if (shift is not { IsActive: true, IsBranchMain: true }) throw new ShiftConflictException("This offer is no longer available.");
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        await Assign(shift, visit, actor, zone, ct);
        visit.UpdatedUtc = DateTime.UtcNow; Db.Store(visit);
        await Db.SaveChangesAsync(ct);
        return Map(visit, shift, zone, zoneId);
    }

    /// <summary>Declines a visit offer. Returns false when the id is not a visit offer for this cleaner.</summary>
    public async Task<bool> Decline(Guid actor, Guid offerId, int version, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var visit = await Db.Query<ShiftOccurrenceException>().FirstOrDefaultAsync(x => x.OfferId == offerId && x.OfferedEmployeeId == actor, ct);
        if (visit is null) return false;
        if (visit.OfferRevision != version) throw new ShiftConflictException("This offer changed. Reload your offers and try again.");
        visit.OfferedEmployeeId = null; visit.OfferId = null; visit.OfferRevision++; visit.UpdatedUtc = DateTime.UtcNow;
        Db.Store(visit);
        if (push is not null) await push.StageStaffAlertAsync(Db, "StaffVisitOfferDeclined", $"{visit.Id}:{visit.OfferRevision}", DateTimeOffset.UtcNow, ct);
        await Db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Gives the visit to a cleaner who accepts work automatically, or leaves an offer waiting for the cleaner's answer.</summary>
    private async Task OfferTo(Shift shift, ShiftOccurrenceException visit, Guid employeeId, TimeZoneInfo zone, CancellationToken ct)
    {
        var employee = await Worker(employeeId, ct);
        // Automatic acceptance only applies to employees who can sign in; others get an ordinary offer.
        if (employee.AutoAcceptWork && !string.IsNullOrWhiteSpace(employee.KeycloakUserId))
        {
            await Assign(shift, visit, employeeId, zone, ct);
            return;
        }
        visit.EmployeeId = null; visit.OfferedEmployeeId = employeeId; visit.OfferRevision++;
        visit.OfferId = Guid.NewGuid();
        if (push is not null)
        {
            var now = DateTime.UtcNow;
            await push.StageAsync(Db, new PushIntent { EmployeeId = employeeId, ShiftId = shift.Id, ExtraExceptionId = visit.Id, OfferId = visit.OfferId,
                Kind = "VisitOffer", EventKey = $"visit-offer:{visit.Id}:{visit.OfferId}", OccurredUtc = now, DueUtc = now, ExpiresUtc = now.AddHours(24) }, ct);
        }
    }

    private async Task Assign(Shift shift, ShiftOccurrenceException visit, Guid employeeId, TimeZoneInfo zone, CancellationToken ct)
    {
        visit.EmployeeId = employeeId; visit.OfferedEmployeeId = null; visit.OfferId = null; visit.OfferRevision++;
        var roster = ShiftVisitRoster.Resolve(shift.Id, [], [visit], [], []);
        await VisitAssignmentSafety.CheckConflicts(shifts, shift, roster, zone, ct);
        if (push is not null) await push.StageScheduleChangeAsync(Db, shift.Id, [employeeId], DateTimeOffset.UtcNow, ct);
    }

    private async Task EnsureUnstarted(Guid shiftId, DateTime startUtc, CancellationToken ct)
    {
        var forms = await Db.Query<QualityCycleForm>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        var invoices = await Db.Query<Invoice>().Where(x => x.Lines.Any(l => l.ShiftId == shiftId)).ToListAsync(ct);
        if (BranchMainShifts.VisitLockReason(forms, invoices, shiftId, startUtc) is { } reason)
            throw new ShiftConflictException($"{reason}, so its cleaner cannot be changed.");
    }

    private async Task<Shift> Main(Guid shiftId, CancellationToken ct)
    {
        var shift = await Db.LoadAsync<Shift>(shiftId, ct) ?? throw new NotFoundException("Shift", shiftId);
        if (!shift.IsBranchMain) throw new ShiftConflictException("This is not a property's main visit shift.");
        return shift;
    }

    private async Task<ShiftOccurrenceException> Visit(Shift shift, Guid visitId, CancellationToken ct)
    {
        var visit = await Db.LoadAsync<ShiftOccurrenceException>(visitId, ct);
        if (visit is null || visit.ShiftId != shift.Id || visit.Status != ShiftOccurrenceExceptionStatus.Extra)
            throw new NotFoundException("Visit", visitId);
        return visit;
    }

    private async Task<Employee> Worker(Guid id, CancellationToken ct)
    {
        var employee = await Db.LoadAsync<Employee>(id, ct);
        if (employee is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(employee.Role))
            throw new ValidationException([new ValidationFailure("EmployeeId", "Select an active cleaner, manager or administrator.")]);
        return employee;
    }

    public static BranchVisitDto Map(ShiftOccurrenceException visit, Shift shift, TimeZoneInfo zone, string zoneId) =>
        new(visit.Id, shift.Id, shift.BranchId, visit.Sequence ?? 0, visit.Source ?? ShiftOccurrenceException.SourceAdminPanel, visit.StartUtc, visit.EndUtc,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(visit.StartUtc, zone)),
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(visit.EndUtc.AddTicks(-1), zone)),
            visit.Notes, visit.EmployeeId, visit.OfferedEmployeeId, visit.OfferRevision, visit.PartnerRequestId,
            visit.EmployeeId.HasValue ? StatusAssigned : visit.OfferedEmployeeId.HasValue ? StatusOffered : StatusUnassigned,
            visit.CleaningType ?? CleaningTypes.Normal, zoneId);
}
