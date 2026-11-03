using CleaningSuite.Application.Notifications;
using System.Globalization;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using Marten;

namespace CleaningSuite.Application.Partners;

/// <summary>Tenant-bound workflow. All mutations share the same reservation as work starts and invoice issue.</summary>
public sealed class PartnerService(IPartnerStore store, IShiftWorkReservation reservation,
    IShiftRepository shifts, ITenantProfileRepository profiles, ITenantContext tenant, IPushOutbox? push = null)
{
    private IDocumentSession Db => store.Session;

    private async Task<(Employee Person, Company Company)> Partner(Guid actor, CancellationToken ct)
    {
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person is not { IsActive: true, IdentitySyncPending: false, Role: Employee.RolePartner } || !person.PartnerCompanyId.HasValue)
            throw new UnauthorizedAccessException("An active company partner account is required.");
        var company = await Db.LoadAsync<Company>(person.PartnerCompanyId.Value, ct);
        if (company is not { IsActive: true, IsPartnerEnabled: true })
            throw new UnauthorizedAccessException("Partner access is disabled for this company.");
        return (person, company);
    }

    private async Task<Employee> Staff(Guid actor, CancellationToken ct)
    {
        // Unbound tenant-owner administration is verified by middleware; partner operations require a real profile.
        var person = await Db.LoadAsync<Employee>(actor, ct);
        if (person is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(person.Role))
            throw new UnauthorizedAccessException("An active staff account is required.");
        return person;
    }

    private async Task RequireManager(Guid actor, CancellationToken ct)
    {
        // Only the authenticated operations controller can supply the legacy, unbound tenant-owner sentinel.
        if (actor == Guid.Empty) return;
        var person = await Staff(actor, ct);
        if (person.Role is not (Employee.RoleAdmin or Employee.RoleManager))
            throw new UnauthorizedAccessException("Administrator or manager access is required.");
    }

    private async Task<Branch> Branch(Guid id, Guid companyId, CancellationToken ct, bool active = true)
    {
        var branch = await Db.LoadAsync<Branch>(id, ct);
        if (branch is null || branch.CompanyId != companyId) throw new NotFoundException("Branch", id);
        if (active && !branch.IsActive) throw new ShiftConflictException("This property is inactive.");
        return branch;
    }

    private async Task<Company> ActiveCompany(Guid id, CancellationToken ct)
    {
        var company = await Db.LoadAsync<Company>(id, ct) ?? throw new NotFoundException("Company", id);
        if (!company.IsActive || !company.IsPartnerEnabled)
            throw new ShiftConflictException("The company must be active and enabled for partner work.");
        return company;
    }

    private async Task<PartnerCleaningRequest> Request(Guid id, Guid? company, CancellationToken ct)
    {
        var request = await Db.LoadAsync<PartnerCleaningRequest>(id, ct);
        if (request is null || company.HasValue && request.CompanyId != company)
            throw new NotFoundException("Cleaning request", id);
        return request;
    }

    private async Task<Employee> PreferenceOwner(Guid actor, CancellationToken ct)
    {
        var employee = await Staff(actor, ct);
        if (string.IsNullOrWhiteSpace(employee.KeycloakUserId))
            throw new UnauthorizedAccessException("An active linked staff account is required.");
        return employee;
    }

    public async Task<WorkPreferenceResponse> WorkPreferences(Guid actor, CancellationToken ct) =>
        new((await PreferenceOwner(actor, ct)).AutoAcceptWork);

    public async Task<WorkPreferenceResponse> SetWorkPreferences(Guid actor, bool autoAcceptWork, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        // Reservation refreshes the tenant session; never use middleware's earlier employee snapshot.
        var employee = await PreferenceOwner(actor, ct);
        if (autoAcceptWork && await Db.Query<PartnerCleaningRequest>()
            .AnyAsync(x => x.Status == "Offered" && x.OfferedEmployeeId == actor, ct))
            throw new ShiftConflictException("Accept or decline your pending offers before enabling automatic acceptance.");
        employee.AutoAcceptWork = autoAcceptWork;
        Db.Store(employee); await Db.SaveChangesAsync(ct);
        return new(employee.AutoAcceptWork);
    }

    public async Task<object> Me(Guid actor, CancellationToken ct)
    {
        var (person, company) = await Partner(actor, ct);
        var (zone, _) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        return new { realm = tenant.TenantId, person = new { person.Id, person.FirstName, person.LastName, person.Role, person.IsActive },
            company = new { company.Id, company.Name, company.IsActive, company.IsPartnerEnabled }, timeZoneId = zone };
    }

    public async Task<IReadOnlyList<BranchDto>> Branches(Guid actor, bool staff, Guid? companyId, CancellationToken ct)
    {
        if (staff) await RequireManager(actor, ct); else companyId = (await Partner(actor, ct)).Company.Id;
        var partnerCompanyIds = (await Db.Query<Company>().Where(x => x.IsPartnerEnabled).ToListAsync(ct)).Select(x => x.Id).ToArray();
        var query = Db.Query<Branch>().AsQueryable();
        query = query.Where(x => partnerCompanyIds.Contains(x.CompanyId));
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        return (await query.OrderBy(x => x.PostalCode).ThenBy(x => x.Name).ToListAsync(ct)).Select(CompanyHandlers.MapBranch).ToArray();
    }

    public async Task<BranchDto> CreateBranch(Guid actor, CreatePartnerBranch body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        string name;
        try { name = Domain.Companies.Branch.PartnerPropertyName(body.Street, body.PostalCode, body.City); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
        if (body.ContactEmail?.Length > 254 || body.ContactEmail is { Length: > 0 } && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(body.ContactEmail))
            throw new ValidationException("Enter a valid property contact email.");
        var branch = Domain.Companies.Branch.Create(company.Id, name, body.Street, body.PostalCode,
            body.City, body.Country, body.ContactPhone);
        try { branch.SetPropertyCounts(body.RoomCount, body.BedCount); branch.SetSize(body.SizeSquareMeters); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
        branch.ContactEmail = body.ContactEmail?.Trim();
        Db.Store(branch);
        Notify(company.Id, branch.Id, null, "PropertyCreated", 1, "Staff");
        await Db.SaveChangesAsync(ct);
        return CompanyHandlers.MapBranch(branch);
    }

    public async Task<BranchDto> Quote(Guid actor, Guid id, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        var branch = await Branch(id, company.Id, ct);
        if (!branch.PricingReady && !branch.QuoteRequestedUtc.HasValue)
        {
            branch.QuoteRequestedUtc = DateTime.UtcNow; branch.UpdatedUtc = DateTime.UtcNow;
            Db.Store(branch); Notify(company.Id, id, null, "QuoteRequested", branch.PricingVersion + 1, "Staff");
            await Db.SaveChangesAsync(ct);
        }
        return CompanyHandlers.MapBranch(branch);
    }

    public async Task<IReadOnlyList<BranchDto>> Quotes(Guid actor, CancellationToken ct)
    {
        await RequireManager(actor, ct);
        var partnerCompanyIds = (await Db.Query<Company>().Where(x => x.IsActive && x.IsPartnerEnabled).ToListAsync(ct)).Select(x => x.Id).ToArray();
        var branches = await Db.Query<Branch>().Where(x => x.IsActive && x.QuoteRequestedUtc != null && partnerCompanyIds.Contains(x.CompanyId)).ToListAsync(ct);
        return branches.Where(x => !x.PricingReady).OrderBy(x => x.PostalCode).ThenBy(x => x.Name).Select(CompanyHandlers.MapBranch).ToArray();
    }

    public async Task<BranchDto> Price(Guid actor, Guid id, PartnerPricing body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct);
        var branch = await Db.LoadAsync<Branch>(id, ct) ?? throw new NotFoundException("Branch", id);
        await ActiveCompany(branch.CompanyId, ct);
        if (!branch.IsActive) throw new ShiftConflictException("Reactivate the property before pricing it.");
        try { branch.SetPricing(body.PriceNet, body.VatRatePercent, body.EstimatedDurationMinutes); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
        Db.Store(branch); Notify(branch.CompanyId, id, null, "PropertyPriced", branch.PricingVersion, "Company");
        await Db.SaveChangesAsync(ct);
        return CompanyHandlers.MapBranch(branch);
    }

    public async Task<PartnerRequestDto> Create(Guid actor, CreatePartnerRequest body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        if (body.ClientRequestId == Guid.Empty) throw new ValidationException("A request identifier is required.");
        var id = PartnerCleaningRequest.StableId($"request:{actor}:{body.ClientRequestId}");
        var existing = await Db.LoadAsync<PartnerCleaningRequest>(id, ct);
        if (existing is not null)
        {
            if (existing.CompanyId != company.Id) throw new ShiftConflictException("This request identifier has already been used.");
            return await Map(existing, false, ct);
        }
        var branch = await Branch(body.BranchId, company.Id, ct);
        if (!branch.PricingReady) throw new ShiftConflictException("Request a quote before booking this property.");
        var request = new PartnerCleaningRequest { Id = id, ClientRequestId = body.ClientRequestId,
            CompanyId = company.Id, BranchId = branch.Id, RequestedByEmployeeId = actor,
            PriceNet = branch.PriceNet!.Value, VatRatePercent = branch.VatRatePercent!.Value,
            EstimatedDurationMinutes = branch.EstimatedDurationMinutes!.Value, PricingVersion = branch.PricingVersion };
        await SetWindow(request, body.FromDate, body.ToDate, body.ExactStartLocalTime, body.Notes, ct);
        Db.Store(request);
        if (!await RoutePreferredCleaner(request, branch, ct)) await NotifyStaffFallback(request, "Requested", branch.PreferredCleanerEmployeeId.HasValue, ct);
        await Db.SaveChangesAsync(ct);
        return await Map(request, false, ct);
    }

    public async Task<PartnerRequestDto> Edit(Guid actor, Guid id, EditPartnerRequest body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        var request = await Request(id, company.Id, ct);
        CheckVersion(request, body.Version); EnsurePartnerEditable(request);
        var branch = await Branch(request.BranchId, company.Id, ct);
        await SetWindow(request, body.FromDate, body.ToDate, body.ExactStartLocalTime, body.Notes, ct);
        var previous = request.OfferedEmployeeId;
        InvalidateOffer(request); request.Status = "Requested"; Touch(request);
        Db.Store(request);
        if (!await RoutePreferredCleaner(request, branch, ct)) await NotifyStaffFallback(request, "RequestChanged", branch.PreferredCleanerEmployeeId.HasValue, ct);
        if (previous.HasValue && previous != request.OfferedEmployeeId && previous != request.AcceptedEmployeeId)
            NotifyRequest(request, "OfferWithdrawn", "Employee", previous);
        await Db.SaveChangesAsync(ct);
        return await Map(request, false, ct);
    }

    public async Task<IReadOnlyList<PartnerRequestDto>> Requests(Guid actor, bool staff, Guid? companyId, CancellationToken ct)
    {
        if (staff) await RequireManager(actor, ct); else companyId = (await Partner(actor, ct)).Company.Id;
        var query = Db.Query<PartnerCleaningRequest>().AsQueryable();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        var items = await query.OrderByDescending(x => x.CreatedUtc).ToListAsync(ct);
        var result = new List<PartnerRequestDto>();
        foreach (var item in items) result.Add(await Map(item, staff, ct));
        return result;
    }

    public async Task<PartnerRequestDto> StaffEdit(Guid actor, Guid id, EditPartnerRequest body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct);
        var request = await Request(id, null, ct); CheckVersion(request, body.Version);
        if (request.Status == "Cancelled") throw new ShiftConflictException("Cancelled requests cannot be rescheduled.");
        await ActiveCompany(request.CompanyId, ct); await Branch(request.BranchId, request.CompanyId, ct);
        await EnsureUnstartedUnbilled(request, ct);
        await SetWindow(request, body.FromDate, body.ToDate, body.ExactStartLocalTime, body.Notes, ct);
        if (request.LinkedShiftId.HasValue)
        {
            var shift = await Db.LoadAsync<Shift>(request.LinkedShiftId.Value, ct);
            if (shift is not null) { shift.Deactivate(); Db.Store(shift);
                if (push is not null && request.AcceptedEmployeeId.HasValue) await push.StageScheduleChangeAsync(Db, shift.Id, [request.AcceptedEmployeeId.Value], DateTimeOffset.UtcNow, ct); }
        }
        var previous = request.OfferedEmployeeId ?? request.AcceptedEmployeeId;
        request.AcceptedEmployeeId = null; request.PlannedStartUtc = null; request.PlannedEndUtc = null;
        InvalidateOffer(request); request.Status = "Requested"; request.DirectlyRouted = false; Touch(request); Db.Store(request);
        NotifyRequest(request, "RequestChanged", "Company");
        if (previous.HasValue) NotifyRequest(request, "OfferWithdrawn", "Employee", previous);
        await Db.SaveChangesAsync(ct); return await Map(request, true, ct);
    }

    public async Task<PartnerRequestDto> Get(Guid actor, Guid id, CancellationToken ct) =>
        await Map(await Request(id, (await Partner(actor, ct)).Company.Id, ct), false, ct);

    public async Task<PartnerRequestDto> Cancel(Guid actor, Guid id, int version, bool staff, string? reason, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        Guid? companyId = null;
        if (staff) { await RequireManager(actor, ct); ValidateText(reason, 1000, "Cancellation reason"); }
        else companyId = (await Partner(actor, ct)).Company.Id;
        var request = await Request(id, companyId, ct); CheckVersion(request, version);
        if (!staff) EnsurePartnerEditable(request);
        if (request.Status == "Cancelled") return await Map(request, staff, ct);
        await EnsureUnstartedUnbilled(request, ct);
        if (request.LinkedShiftId.HasValue)
        {
            var shift = await Db.LoadAsync<Shift>(request.LinkedShiftId.Value, ct);
            if (shift is not null) { shift.Deactivate(); Db.Store(shift);
                if (push is not null && request.AcceptedEmployeeId.HasValue) await push.StageScheduleChangeAsync(Db, shift.Id, [request.AcceptedEmployeeId.Value], DateTimeOffset.UtcNow, ct); }
        }
        request.Status = "Cancelled"; request.CancellationReason = reason?.Trim();
        var oldEmployee = request.OfferedEmployeeId ?? request.AcceptedEmployeeId;
        InvalidateOffer(request); Touch(request); Db.Store(request);
        NotifyRequest(request, "RequestCancelled", staff || request.DirectlyRouted ? "Company" : "Staff");
        if (oldEmployee.HasValue) NotifyRequest(request, "RequestCancelled", "Employee", oldEmployee);
        await Db.SaveChangesAsync(ct);
        return await Map(request, staff, ct);
    }

    public async Task<PartnerRequestDto> Offer(Guid actor, Guid id, PartnerOffer body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct);
        var request = await Request(id, null, ct); CheckVersion(request, body.Version);
        await ActiveCompany(request.CompanyId, ct); await Branch(request.BranchId, request.CompanyId, ct);
        if (request.Status == "Cancelled") throw new ShiftConflictException("Cancelled requests cannot be offered.");
        await EnsureUnstartedUnbilled(request, ct);
        var employee = await Db.LoadAsync<Employee>(body.EmployeeId, ct);
        if (employee is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(employee.Role))
            throw new ValidationException("Select an active cleaner, manager or administrator.");
        if (employee.AutoAcceptWork && string.IsNullOrWhiteSpace(employee.KeycloakUserId))
            throw new ShiftConflictException("The employee must have an active linked login before work can be automatically accepted.");
        DateTime? planned = null;
        if (!string.IsNullOrWhiteSpace(body.PlannedStartLocal)) planned = await Plan(request, body.PlannedStartLocal, ct);
        else if (request.FromDate == request.ToDate && request.ExactStartLocalTime is not null)
            planned = await Plan(request, $"{request.FromDate:yyyy-MM-dd}T{request.ExactStartLocalTime}", ct);
        if (employee.AutoAcceptWork && !planned.HasValue)
            throw new ShiftConflictException("Set an exact planned start before offering work to an employee with automatic acceptance enabled.");
        if (request.LinkedShiftId.HasValue)
        {
            var shift = await Db.LoadAsync<Shift>(request.LinkedShiftId.Value, ct);
            if (shift is not null) { shift.Deactivate(); Db.Store(shift);
                if (push is not null && request.AcceptedEmployeeId.HasValue && (!employee.AutoAcceptWork || request.AcceptedEmployeeId != employee.Id)) await push.StageScheduleChangeAsync(Db, shift.Id, [request.AcceptedEmployeeId.Value], DateTimeOffset.UtcNow, ct); }
        }
        var previousEmployee = request.OfferedEmployeeId ?? request.AcceptedEmployeeId;
        request.AcceptedEmployeeId = null; request.PlannedStartUtc = null; request.PlannedEndUtc = null;
        request.DirectlyRouted = false;
        Touch(request); request.OfferId = PartnerCleaningRequest.StableId($"offer:{request.Id}:{request.Revision}");
        request.OfferedEmployeeId = employee.Id; request.OfferPlannedStartUtc = planned; request.Status = "Offered";
        if (employee.AutoAcceptWork)
        {
            await MaterializeAcceptance(request, employee.Id, planned!.Value, ct, automatic: true);
            await NotifyAutomaticAcceptance(request, employee.Id, ct);
        }
        else
        {
            Db.Store(request); NotifyRequest(request, "OfferCreated", "Employee", employee.Id);
            NotifyRequest(request, "AwaitingAcceptance", "Company");
            if (push is not null) await push.StagePartnerOfferAsync(Db, request.Id, request.Revision, request.OfferId.Value, employee.Id, DateTimeOffset.UtcNow, ct);
        }
        if (previousEmployee.HasValue && previousEmployee != employee.Id) NotifyRequest(request, "OfferWithdrawn", "Employee", previousEmployee);

        await Db.SaveChangesAsync(ct);
        return await Map(request, true, ct);
    }

    public async Task<IReadOnlyList<PartnerCleanerOfferDto>> Offers(Guid actor, CancellationToken ct)
    {
        await Staff(actor, ct);
        var requests = await Db.Query<PartnerCleaningRequest>().Where(x => x.Status == "Offered" && x.OfferedEmployeeId == actor).ToListAsync(ct);
        var (zoneId, _) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var result = new List<PartnerCleanerOfferDto>();
        foreach (var item in requests)
        {
            var company = await Db.LoadAsync<Company>(item.CompanyId, ct);
            var branch = await Db.LoadAsync<Branch>(item.BranchId, ct);
            if (company is not { IsActive: true, IsPartnerEnabled: true } || branch is not { IsActive: true }) continue;
            result.Add(new(item.OfferId!.Value, item.Id, item.Revision, branch.Name, branch.Street,
                branch.PostalCode, branch.City, item.FromDate, item.ToDate, item.ExactStartLocalTime,
                item.OfferPlannedStartUtc, item.EstimatedDurationMinutes, item.Notes, zoneId));
        }
        return result;
    }

    public async Task<PartnerRequestDto> Accept(Guid actor, Guid offerId, PartnerAcceptance body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await Staff(actor, ct);
        var request = await Db.Query<PartnerCleaningRequest>().FirstOrDefaultAsync(x => x.OfferId == offerId && x.OfferedEmployeeId == actor, ct)
            ?? throw new NotFoundException("Cleaning offer", offerId);
        // A response lost after commit may be retried with the accepted offer version; never materialize twice.
        if (request.Status == "Accepted" && request.AcceptedEmployeeId == actor && body.Version == request.Revision - 1)
            return await Map(request, false, ct);
        CheckVersion(request, body.Version);
        if (request.Status != "Offered") throw new ShiftConflictException("This offer is no longer awaiting acceptance.");
        await ActiveCompany(request.CompanyId, ct);
        await Branch(request.BranchId, request.CompanyId, ct);
        await EnsureUnstartedUnbilled(request, ct);
        DateTime start;
        if (request.OfferPlannedStartUtc.HasValue)
        {
            start = request.OfferPlannedStartUtc.Value;
            if (!string.IsNullOrWhiteSpace(body.PlannedStartLocal) && await Plan(request, body.PlannedStartLocal, ct) != start)
                throw new ValidationException("Accept the planned time set by your manager.");
        }
        else start = await Plan(request, body.PlannedStartLocal, ct);
        await MaterializeAcceptance(request, actor, start, ct);
        await Db.SaveChangesAsync(ct);
        return await Map(request, false, ct);
    }

    // Caller holds the tenant work reservation; all documents and notifications commit together.
    private async Task MaterializeAcceptance(PartnerCleaningRequest request, Guid actor, DateTime start, CancellationToken ct, bool automatic = false)
    {
        await Staff(actor, ct);
        await ActiveCompany(request.CompanyId, ct);
        var branch = await Branch(request.BranchId, request.CompanyId, ct);
        await EnsureUnstartedUnbilled(request, ct);
        var end = start.AddMinutes(request.EstimatedDurationMinutes);
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var localFrom = TenantTimeZone.LocalDate(start, zone);
        var localUntil = TenantTimeZone.LocalDate(end.AddTicks(-1), zone);
        var shiftId = request.LinkedShiftId ?? PartnerCleaningRequest.StableId($"partner-shift:{request.Id}");
        var existingShift = await Db.LoadAsync<Shift>(shiftId, ct);
        // A failed automatic attempt may be caught and committed as an offer. Validate
        // detached candidates first: Marten dirty-tracks loaded history even without Store.
        var candidate = Shift.Create(request.CompanyId, request.BranchId,
            existingShift?.Name ?? $"Cleaning — {branch.Name}", existingShift?.Schedule ?? new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible },
            request.Notes, localFrom, localUntil);
        candidate.Id = shiftId; candidate.PartnerRequestId = request.Id;
        var occurrenceId = PartnerCleaningRequest.StableId($"partner-occurrence:{request.Id}");
        var candidateOccurrence = ShiftOccurrenceException.Create(shiftId, null, start, end,
            ShiftOccurrenceExceptionStatus.Extra, "Accepted partner cleaning", actor);
        candidateOccurrence.Id = occurrenceId;
        var roster = ShiftVisitRoster.Resolve(shiftId, [], [candidateOccurrence], [], []);
        await VisitAssignmentSafety.CheckConflicts(shifts, candidate, roster, zone, ct);

        var shift = existingShift ?? candidate;
        shift.Id = shiftId; shift.PartnerRequestId = request.Id; shift.IsActive = true;
        shift.ValidFrom = localFrom; shift.ValidUntil = localUntil; shift.Notes = request.Notes;
        var occurrence = await Db.LoadAsync<ShiftOccurrenceException>(occurrenceId, ct) ?? candidateOccurrence;
        occurrence.Update(shiftId, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "Accepted partner cleaning", actor);
        var assignments = await Db.Query<ShiftAssignment>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        foreach (var previous in assignments) { previous.EndFrom(DateTime.UtcNow, DateTime.UtcNow); Db.Store(previous); }
        var assignment = assignments.FirstOrDefault(x => x.EmployeeId == actor) ?? ShiftAssignment.Create(shiftId, actor, "Accepted partner cleaning");
        assignment.IsActive = true; assignment.AssignedAtUtc = start; assignment.UnassignedAtUtc = null;
        assignment.Periods = [new() { FromUtc = start }];
        var billing = await Db.Query<ShiftBillingProfile>().FirstOrDefaultAsync(x => x.ShiftId == shiftId, ct)
            ?? new ShiftBillingProfile { ShiftId = shiftId };
        billing.Plans = [new("PerOccurrence", request.PriceNet, request.VatRatePercent, DateOnly.FromDateTime(TenantTimeZone.LocalDate(start, zone)))];
        billing.Overrides = [];
        request.LinkedShiftId = shiftId; request.AcceptedEmployeeId = actor;
        request.PlannedStartUtc = start; request.PlannedEndUtc = end;
        request.FirstAcceptedAtUtc ??= DateTime.UtcNow; request.Status = "Accepted"; Touch(request);
        // A single Marten commit owns every document. Repository methods that commit individually are deliberately avoided.
        Db.Store(shift); Db.Store(occurrence); Db.Store(assignment); Db.Store(billing); Db.Store(request);
        if (push is not null) await push.StageScheduleChangeAsync(Db, shiftId, assignments.Select(x => x.EmployeeId).Append(actor).Where(id => !automatic || id != actor), DateTimeOffset.UtcNow, ct);
        NotifyRequest(request, "RequestAccepted", "Company");
        if (!request.DirectlyRouted) NotifyRequest(request, "RequestAccepted", "Staff");
    }

    private async Task NotifyAutomaticAcceptance(PartnerCleaningRequest request, Guid employeeId, CancellationToken ct)
    {
        NotifyRequest(request, "WorkAutoAccepted", "Employee", employeeId);
        if (push is not null)
        {
            var now = DateTime.UtcNow;
            await push.StageAsync(Db, new PushIntent { EmployeeId = employeeId, PartnerRequestId = request.Id,
                RequestRevision = request.Revision, Kind = "WorkAutoAccepted", EventKey = $"work-auto-accepted:{request.Id}:{request.Revision}:{employeeId}",
                OccurredUtc = now, DueUtc = now, ExpiresUtc = now.AddHours(24) }, ct);
        }
    }

    private async Task NotifyStaffFallback(PartnerCleaningRequest request, string kind, bool preferredUnavailable, CancellationToken ct)
    {
        NotifyRequest(request, preferredUnavailable ? "PreferredCleanerUnavailable" : kind, "Staff");
        if (push is not null) await push.StagePartnerRequestAsync(Db, request.Id, request.Revision, DateTimeOffset.UtcNow, ct);
    }

    private async Task<bool> RoutePreferredCleaner(PartnerCleaningRequest request, Branch branch, CancellationToken ct)
    {
        request.DirectlyRouted = false;
        if (!branch.PreferredCleanerEmployeeId.HasValue) return false;
        var employee = await Db.LoadAsync<Employee>(branch.PreferredCleanerEmployeeId.Value, ct);
        if (employee is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(employee.Role)
            || string.IsNullOrWhiteSpace(employee.KeycloakUserId)) return false;
        request.DirectlyRouted = true;
        request.OfferId = PartnerCleaningRequest.StableId($"offer:{request.Id}:{request.Revision}");
        request.OfferedEmployeeId = employee.Id; request.Status = "Offered";
        if (request.FromDate == request.ToDate && request.ExactStartLocalTime is not null)
            request.OfferPlannedStartUtc = await Plan(request, $"{request.FromDate:yyyy-MM-dd}T{request.ExactStartLocalTime}", ct);
        if (employee.AutoAcceptWork && request.OfferPlannedStartUtc is { } start && start > DateTime.UtcNow)
        {
            try
            {
                await MaterializeAcceptance(request, employee.Id, start, ct, automatic: true);
                await NotifyAutomaticAcceptance(request, employee.Id, ct);
            }
            catch (ValidationException) when (request.Status == "Offered") { }
            catch (ShiftConflictException) when (request.Status == "Offered") { }
        }
        if (request.Status == "Offered")
        {
            Db.Store(request); NotifyRequest(request, "OfferCreated", "Employee", employee.Id);
            NotifyRequest(request, "AwaitingAcceptance", "Company");
            if (push is not null) await push.StagePartnerOfferAsync(Db, request.Id, request.Revision, request.OfferId!.Value, employee.Id, DateTimeOffset.UtcNow, ct);
        }
        Db.Store(request);
        return true;
    }

    public async Task Decline(Guid actor, Guid offerId, PartnerDecline body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await Staff(actor, ct);
        var request = await Db.Query<PartnerCleaningRequest>().FirstOrDefaultAsync(x => x.OfferId == offerId && x.OfferedEmployeeId == actor, ct)
            ?? throw new NotFoundException("Cleaning offer", offerId);
        CheckVersion(request, body.Version);
        if (request.Status != "Offered") throw new ShiftConflictException("This offer is no longer awaiting acceptance.");
        if (body.Reason?.Length > 1000) throw new ValidationException("Use at most 1000 characters for the reason.");
        InvalidateOffer(request); request.Status = "Requested"; request.DirectlyRouted = false; Touch(request); Db.Store(request);
        await NotifyStaffFallback(request, "OfferDeclined", false, ct); NotifyRequest(request, "AwaitingAssignment", "Company");
        await Db.SaveChangesAsync(ct);
    }

    public async Task<PartnerChangeDto> Change(Guid actor, Guid id, bool property, PartnerMessage body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct); ValidateText(body.Message, 2000, "Change request");
        var request = property ? null : await Request(id, company.Id, ct);
        var branch = await Branch(property ? id : request!.BranchId, company.Id, ct, false);
        var change = new PartnerChangeRequest { CompanyId = company.Id, BranchId = branch.Id,
            RequestId = request?.Id, RequestedByEmployeeId = actor, Kind = property ? "Property" : "Cleaning",
            Message = body.Message.Trim() };
        Db.Store(change); Notify(company.Id, branch.Id, request?.Id, "ChangeRequested", 1, "Staff", null, change.Id);
        await Db.SaveChangesAsync(ct); return MapChange(change);
    }

    public async Task<IReadOnlyList<PartnerChangeDto>> Changes(Guid actor, CancellationToken ct)
    {
        await RequireManager(actor, ct);
        return (await Db.Query<PartnerChangeRequest>().OrderByDescending(x => x.CreatedUtc).ToListAsync(ct)).Select(MapChange).ToArray();
    }

    public async Task<IReadOnlyList<PartnerChangeDto>> OwnChanges(Guid actor, CancellationToken ct)
    {
        var (_, company) = await Partner(actor, ct);
        return (await Db.Query<PartnerChangeRequest>().Where(x => x.CompanyId == company.Id)
            .OrderByDescending(x => x.CreatedUtc).ToListAsync(ct)).Select(MapChange).ToArray();
    }

    public async Task<PartnerChangeDto> Resolve(Guid actor, Guid id, PartnerResolution body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct); ValidateText(body.Resolution, 2000, "Resolution");
        var change = await Db.LoadAsync<PartnerChangeRequest>(id, ct) ?? throw new NotFoundException("Change request", id);
        if (change.ResolvedUtc.HasValue) throw new ShiftConflictException("This change request has already been resolved.");
        change.ResolvedUtc = DateTime.UtcNow; change.Resolution = body.Resolution.Trim();
        Db.Store(change); Notify(change.CompanyId, change.BranchId, change.RequestId, "ChangeResolved", 1, "Company", null, change.Id);
        await Db.SaveChangesAsync(ct); return MapChange(change);
    }

    public async Task<object> CompanyShifts(Guid actor, CancellationToken ct)
    {
        var (_, company) = await Partner(actor, ct);
        var items = await Db.Query<Shift>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
        var branches = await Db.Query<Branch>().Where(x => x.CompanyId == company.Id).ToListAsync(ct);
        return items.Select(x => new { x.Id, x.BranchId, branchName = branches.FirstOrDefault(b => b.Id == x.BranchId)?.Name ?? "Property",
            x.Name, x.IsActive, x.Schedule, x.ValidFrom, x.ValidUntil }).ToArray();
    }

    private async Task EnsureUnstartedUnbilled(PartnerCleaningRequest request, CancellationToken ct)
    {
        if (!request.LinkedShiftId.HasValue) return;
        var id = request.LinkedShiftId.Value;
        if (await Db.Query<QualityCycleForm>().AnyAsync(x => x.ShiftId == id && x.ShiftDeletedUtc == null && (x.StartedAtUtc != null || x.IsSubmitted), ct))
            throw new ShiftConflictException("Work has started. Use a separate staff correction process; this visit cannot be reassigned, moved or cancelled.");
        var invoices = await Db.Query<Invoice>().Where(x => x.CompanyId == request.CompanyId).ToListAsync(ct);
        if (invoices.Any(x => x.Status is "Issued" or "Paid" && x.Lines.Any(l => l.ShiftId == id)))
            throw new ShiftConflictException("This visit has already been invoiced and cannot be changed.");
    }

    private async Task SetWindow(PartnerCleaningRequest request, DateOnly from, DateOnly to, string? exact, string? notes, CancellationToken ct)
    {
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        ValidateWindow(from, to, exact, request.EstimatedDurationMinutes, zone);
        if (notes?.Length > 4000) throw new ValidationException("Use at most 4000 characters for cleaning instructions.");
        request.FromDate = from; request.ToDate = to; request.ExactStartLocalTime = string.IsNullOrWhiteSpace(exact) ? null : exact;
        request.Notes = notes?.Trim();
    }

    public static void ValidateWindow(DateOnly from, DateOnly to, string? exact, int duration, TimeZoneInfo zone)
    {
        if (from.Year < 2000 || to.Year > 2099 || to < from || to.DayNumber - from.DayNumber > 366)
            throw new ValidationException("Choose a supported inclusive date range of at most 367 days.");
        if (duration is < 1 or > 1440) throw new ValidationException("The property needs an estimated cleaning duration.");
        if (!string.IsNullOrWhiteSpace(exact))
        {
            if (from != to || !TimeOnly.TryParseExact(exact, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new ValidationException("An exact appointment requires one date and a time in HH:mm format.");
            var local = from.ToDateTime(time);
            var utc = LocalToUtc(local, zone);
            if (utc.AddMinutes(duration) > TenantTimeZone.StartOfLocalDay(to.ToDateTime(TimeOnly.MinValue).AddDays(1), zone))
                throw new ValidationException("The complete cleaning must fit within the requested day.");
        }
    }

    public static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ValidationException("Choose a local time outside the daylight-saving clock change.");
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private async Task<DateTime> Plan(PartnerCleaningRequest request, string? value, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ValidationException("Choose the cleaning start date and time in the tenant time zone.");
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var start = LocalToUtc(local, zone);
        var lower = TenantTimeZone.StartOfLocalDay(request.FromDate.ToDateTime(TimeOnly.MinValue), zone);
        var upper = TenantTimeZone.StartOfLocalDay(request.ToDate.ToDateTime(TimeOnly.MinValue).AddDays(1), zone);
        if (start < lower || start.AddMinutes(request.EstimatedDurationMinutes) > upper)
            throw new ValidationException("The entire visit must fit within the requested dates.");
        if (request.FromDate == request.ToDate && request.ExactStartLocalTime is not null && local != request.FromDate.ToDateTime(TimeOnly.ParseExact(request.ExactStartLocalTime, "HH:mm", CultureInfo.InvariantCulture)))
            throw new ValidationException("This cleaning has an exact appointment time.");
        return start;
    }

    private async Task<PartnerRequestDto> Map(PartnerCleaningRequest item, bool staff, CancellationToken ct)
    {
        var branch = await Db.LoadAsync<Branch>(item.BranchId, ct);
        var employee = item.AcceptedEmployeeId.HasValue ? await Db.LoadAsync<Employee>(item.AcceptedEmployeeId.Value, ct) : null;
        var forms = item.LinkedShiftId.HasValue
            ? await Db.Query<QualityCycleForm>().Where(x => x.ShiftId == item.LinkedShiftId.Value && x.ShiftDeletedUtc == null).ToListAsync(ct) : [];
        // JSON timestamps use the existing repository's UTC convention. Filter the sole linked visit in memory
        // rather than binding a UTC DateTime to Marten's timestamp-without-time-zone JSON comparison.
        forms = forms.Where(x => x.EmployeeId == item.AcceptedEmployeeId && x.ShiftOccurrenceUtc == item.PlannedStartUtc).ToList();
        var started = forms.Where(x => x.StartedAtUtc.HasValue).Select(x => x.StartedAtUtc).Min();
        var ended = forms.Where(x => x.IsSubmitted && x.EndedAtUtc.HasValue).Select(x => x.EndedAtUtc).Max();
        var status = item.Status;
        if (status == "Accepted" && started.HasValue) status = forms.Count > 0 && forms.All(x => x.IsSubmitted) ? "Completed" : "InProgress";
        return new(item.Id, item.Revision, item.CompanyId, item.BranchId, branch?.Name ?? "Property", item.FromDate,
            item.ToDate, item.ExactStartLocalTime, item.Notes, item.PriceNet, item.VatRatePercent,
            item.EstimatedDurationMinutes, status, item.FirstAcceptedAtUtc, item.LinkedShiftId, item.PlannedStartUtc,
            item.PlannedEndUtc, employee is null ? null : $"{employee.FirstName} {employee.LastName}".Trim(), started,
            status == "Completed" ? ended : null, staff ? item.OfferedEmployeeId : null, staff ? item.OfferId : null);
    }

    private static PartnerChangeDto MapChange(PartnerChangeRequest x) => new(x.Id, x.CompanyId, x.BranchId,
        x.RequestId, x.Kind, x.Message, x.CreatedUtc, x.ResolvedUtc, x.Resolution);
    private static void ValidateText(string? value, int max, string name)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ValidationException($"{name} is required and must be at most {max} characters."); }
    private static void CheckVersion(PartnerCleaningRequest request, int version)
    { if (request.Revision != version) throw new ShiftConflictException("This request changed. Reload it before making another change."); }
    private static void EnsurePartnerEditable(PartnerCleaningRequest request)
    { if (request.FirstAcceptedAtUtc.HasValue || request.Status == "Cancelled") throw new ShiftConflictException("This request can no longer be edited directly. Ask staff for a change."); }
    private static void InvalidateOffer(PartnerCleaningRequest request)
    { request.OfferId = null; request.OfferedEmployeeId = null; request.OfferPlannedStartUtc = null; }
    private static void Touch(PartnerCleaningRequest request) { request.Revision++; request.UpdatedUtc = DateTime.UtcNow; }
    private void NotifyRequest(PartnerCleaningRequest request, string kind, string audience, Guid? employee = null) =>
        Notify(request.CompanyId, request.BranchId, request.Id, kind, request.Revision, audience, employee);
    private void Notify(Guid company, Guid branch, Guid? request, string kind, int revision, string audience,
        Guid? employee = null, Guid? eventId = null)
    {
        Db.Store(new PartnerNotification { Id = PartnerCleaningRequest.StableId($"notice:{eventId ?? request ?? branch}:{kind}:{revision}:{audience}:{employee}"),
            CompanyId = company, BranchId = branch, RequestId = request, Kind = kind, Revision = revision,
            Audience = audience, RecipientEmployeeId = employee });
    }
}
