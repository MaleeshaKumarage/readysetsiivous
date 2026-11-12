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

    private async Task ReleaseShiftAssignments(Guid shiftId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var active = await Db.Query<ShiftAssignment>().Where(a => a.ShiftId == shiftId && a.IsActive).ToListAsync(ct);
        foreach (var assignment in active) { assignment.Release(now); Db.Store(assignment); }
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
        if (autoAcceptWork && (await Db.Query<PartnerCleaningRequest>()
            .AnyAsync(x => x.Status == "Offered" && x.OfferedEmployeeId == actor, ct)
            || await Db.Query<ShiftOccurrenceException>().AnyAsync(x => x.OfferedEmployeeId == actor && x.OfferId != null, ct)))
            throw new ShiftConflictException("Accept or decline your pending offers before enabling automatic acceptance.");
        employee.AutoAcceptWork = autoAcceptWork;
        employee.AutoAcceptWorkChosenUtc = DateTime.UtcNow;
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
        return (await query.ToListAsync(ct)).Select(CompanyHandlers.MapBranch).ToArray();
    }

    public async Task<BranchDto> CreateBranch(Guid actor, CreatePartnerBranch body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        ValidateBranchDetails(body);
        var branch = Domain.Companies.Branch.Create(company.Id, body.Name, body.Street, body.PostalCode,
            body.City, body.Country, body.ContactPhone);
        try { branch.SetPropertyCounts(body.RoomCount, body.BedCount); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
        branch.ContactEmail = body.ContactEmail?.Trim();
        Db.Store(branch);
        Notify(company.Id, branch.Id, null, "PropertyCreated", 1, "Staff");
        await Db.SaveChangesAsync(ct);
        return CompanyHandlers.MapBranch(branch);
    }

    // Owners correct a property's details (never its price, company or active flag). Same validation as creation.
    public async Task<BranchDto> UpdateBranch(Guid actor, Guid id, CreatePartnerBranch body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        var branch = await Branch(id, company.Id, ct);
        ValidateBranchDetails(body);
        try { branch.SetPropertyCounts(body.RoomCount, body.BedCount); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
        branch.Update(body.Name, body.Street, body.PostalCode, body.City, body.Country, body.ContactPhone, branch.IsActive);
        branch.ContactEmail = body.ContactEmail?.Trim();
        Db.Store(branch);
        await Db.SaveChangesAsync(ct);
        return CompanyHandlers.MapBranch(branch);
    }

    public async Task<BranchDto> WithdrawQuote(Guid actor, Guid id, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        var branch = await Branch(id, company.Id, ct);
        if (!branch.HasPendingQuote) throw new ShiftConflictException("There is no pending quote request to withdraw.");
        branch.WithdrawQuote(DateTime.UtcNow);
        Db.Store(branch);
        await Db.SaveChangesAsync(ct);
        return CompanyHandlers.MapBranch(branch);
    }

    // Staff decline a quote that cannot be priced. The reason is required and shown to the owner.
    public async Task<BranchDto> RejectQuote(Guid actor, Guid id, PartnerQuoteRejection body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct);
        ValidateText(body.Reason, 1000, "Rejection reason");
        var branch = await Db.LoadAsync<Branch>(id, ct) ?? throw new NotFoundException("Branch", id);
        await ActiveCompany(branch.CompanyId, ct);
        if (!branch.HasPendingQuote) throw new ShiftConflictException("This property has no pending quote request.");
        branch.RejectQuote(body.Reason, DateTime.UtcNow);
        Db.Store(branch);
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
            branch.QuoteRejectedUtc = null; branch.QuoteRejectionReason = null;
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
        return branches.Where(x => !x.PricingReady).Select(CompanyHandlers.MapBranch).ToArray();
    }

    public async Task<BranchDto> Price(Guid actor, Guid id, PartnerPricing body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await RequireManager(actor, ct);
        var branch = await Db.LoadAsync<Branch>(id, ct) ?? throw new NotFoundException("Branch", id);
        await ActiveCompany(branch.CompanyId, ct);
        if (!branch.IsActive) throw new ShiftConflictException("Reactivate the property before pricing it.");
        bool changed;
        try { changed = branch.SetPricing(body.PriceNet, body.VatRatePercent, body.EstimatedDurationMinutes); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
        // An unchanged re-save is a successful no-op: no write, no new revision and no repeated notice.
        if (!changed) return CompanyHandlers.MapBranch(branch);
        // A notice already stored for this revision keeps its delivery state; only a genuinely new revision adds one.
        var noticeId = NoticeId(id, null, "PropertyPriced", branch.PricingVersion, "Company", null);
        Db.Store(branch);
        if (await Db.LoadAsync<PartnerNotification>(noticeId, ct) is null)
            Notify(branch.CompanyId, id, null, "PropertyPriced", branch.PricingVersion, "Company");
        await Db.SaveChangesAsync(ct);
        return CompanyHandlers.MapBranch(branch);
    }

    public async Task<PartnerRequestDto> Create(Guid actor, CreatePartnerRequest body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        // Name every missing field, so the caller sees all of them at once rather than the first one.
        var missing = new List<FluentValidation.Results.ValidationFailure>();
        if (body.ClientRequestId == Guid.Empty) missing.Add(new("ClientRequestId", "A request identifier is required."));
        if (body.BranchId == Guid.Empty) missing.Add(new("BranchId", "Choose the property for this cleaning."));
        if (body.FromDate == default) missing.Add(new("FromDate", "Choose the first day of the cleaning."));
        if (body.ToDate == default) missing.Add(new("ToDate", "Choose the last day of the cleaning."));
        if (missing.Count > 0) throw new ValidationException(missing);
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
            PriceNet = branch.CurrentPriceNet!.Value, VatRatePercent = branch.CurrentVatRatePercent!.Value,
            EstimatedDurationMinutes = branch.CurrentEstimatedDurationMinutes!.Value, PricingVersion = branch.PricingVersion };
        request.CleaningType = CleaningOf(body.CleaningType);
        await SetWindow(request, body.FromDate, body.ToDate, body.ExactStartLocalTime, body.Notes, ct);
        Db.Store(request); NotifyRequest(request, "Requested", "Staff");
        if (push is not null) await push.StagePartnerRequestAsync(Db, request.Id, request.Revision, DateTimeOffset.UtcNow, ct);
        await Db.SaveChangesAsync(ct);
        return await Map(request, false, ct);
    }

    public async Task<PartnerRequestDto> Edit(Guid actor, Guid id, EditPartnerRequest body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        var request = await Request(id, company.Id, ct);
        CheckVersion(request, body.Version); EnsurePartnerEditable(request);
        await Branch(request.BranchId, company.Id, ct);
        if (body.CleaningType is not null) request.CleaningType = CleaningOf(body.CleaningType);
        await SetWindow(request, body.FromDate, body.ToDate, body.ExactStartLocalTime, body.Notes, ct);
        var previous = request.OfferedEmployeeId;
        InvalidateOffer(request); request.Status = "Requested"; Touch(request);
        Db.Store(request); NotifyRequest(request, "RequestChanged", "Staff");
        if (previous.HasValue) NotifyRequest(request, "OfferWithdrawn", "Employee", previous);
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
        if (body.CleaningType is not null) request.CleaningType = CleaningOf(body.CleaningType);
        await SetWindow(request, body.FromDate, body.ToDate, body.ExactStartLocalTime, body.Notes, ct);
        await ReleaseVisit(request, notifyAccepted: true, removeOccurrence: true, ct);
        var previous = request.OfferedEmployeeId ?? request.AcceptedEmployeeId;
        request.AcceptedEmployeeId = null; request.PlannedStartUtc = null; request.PlannedEndUtc = null;
        InvalidateOffer(request); request.Status = "Requested"; Touch(request); Db.Store(request);
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
        if (staff) await RequireManager(actor, ct);
        else companyId = (await Partner(actor, ct)).Company.Id;
        reason = reason?.Trim();
        ValidateText(reason, 1000, "Cancellation reason");
        var request = await Request(id, companyId, ct); CheckVersion(request, version);
        if (!staff) EnsurePartnerEditable(request);
        if (request.Status == "Cancelled") return await Map(request, staff, ct);
        await EnsureUnstartedUnbilled(request, ct);
        await ReleaseVisit(request, notifyAccepted: true, removeOccurrence: true, ct);
        request.Status = "Cancelled"; request.CancellationReason = reason?.Trim();
        var oldEmployee = request.OfferedEmployeeId ?? request.AcceptedEmployeeId;
        InvalidateOffer(request); Touch(request); Db.Store(request);
        NotifyRequest(request, "RequestCancelled", staff ? "Company" : "Staff");
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
        // Automatic acceptance only applies to employees who can sign in to accept; others get an ordinary offer.
        var automatic = employee.AutoAcceptWork && !string.IsNullOrWhiteSpace(employee.KeycloakUserId);
        DateTime? planned = null;
        if (!string.IsNullOrWhiteSpace(body.PlannedStartLocal)) planned = await Plan(request, body.PlannedStartLocal, ct);
        else if (request.FromDate == request.ToDate && request.ExactStartLocalTime is not null)
            planned = await Plan(request, $"{request.FromDate:yyyy-MM-dd}T{request.ExactStartLocalTime}", ct);
        // A stored clock time on a multi-day window needs an explicit date before work is accepted automatically.
        if (automatic && !planned.HasValue && request.ExactStartLocalTime is not null)
            throw new ShiftConflictException("Choose the exact date and time before automatic acceptance.");
        // Automatic acceptance writes the visit again straight away, so its occurrence is updated in place, not removed.
        await ReleaseVisit(request, notifyAccepted: !automatic || request.AcceptedEmployeeId != employee.Id, removeOccurrence: !automatic, ct);
        var previousEmployee = request.OfferedEmployeeId ?? request.AcceptedEmployeeId;
        request.AcceptedEmployeeId = null; request.PlannedStartUtc = null; request.PlannedEndUtc = null;
        Touch(request); request.OfferId = PartnerCleaningRequest.StableId($"offer:{request.Id}:{request.Revision}");
        request.OfferedEmployeeId = employee.Id; request.OfferPlannedStartUtc = planned; request.Status = "Offered";
        if (automatic)
        {
            await MaterializeAcceptance(request, employee.Id, planned, ct, automatic: true);
            NotifyRequest(request, "WorkAutoAccepted", "Employee", employee.Id);
            if (push is not null)
            {
                var now = DateTime.UtcNow;
                await push.StageAsync(Db, new PushIntent { EmployeeId = employee.Id, PartnerRequestId = request.Id,
                    RequestRevision = request.Revision, Kind = "WorkAutoAccepted", EventKey = $"work-auto-accepted:{request.Id}:{request.Revision}:{employee.Id}",
                    OccurredUtc = now, DueUtc = now, ExpiresUtc = now.AddHours(24) }, ct);
            }
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
        // Each company's general instructions are read once, however many offers it has.
        var generalByCompany = new Dictionary<Guid, string?>();
        foreach (var item in requests)
        {
            var company = await Db.LoadAsync<Company>(item.CompanyId, ct);
            var branch = await Db.LoadAsync<Branch>(item.BranchId, ct);
            if (company is not { IsActive: true, IsPartnerEnabled: true } || branch is not { IsActive: true }) continue;
            if (!generalByCompany.TryGetValue(item.CompanyId, out var general))
                generalByCompany[item.CompanyId] = general = (await Db.LoadAsync<PartnerPortalSettings>(item.CompanyId, ct))?.GeneralInstructions;
            result.Add(new(item.OfferId!.Value, item.Id, item.Revision, branch.Name, branch.Street,
                branch.PostalCode, branch.City, item.FromDate, item.ToDate, item.ExactStartLocalTime,
                item.OfferPlannedStartUtc, item.EstimatedDurationMinutes, VisitNotes(general, item.Notes), zoneId));
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
        // No start time means a flexible visit: the cleaner works any time within the requested dates.
        DateTime? start = request.OfferPlannedStartUtc;
        if (start.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(body.PlannedStartLocal) && await Plan(request, body.PlannedStartLocal, ct) != start.Value)
                throw new ValidationException("Accept the planned time set by your manager.");
        }
        else if (!string.IsNullOrWhiteSpace(body.PlannedStartLocal)) start = await Plan(request, body.PlannedStartLocal, ct);
        await MaterializeAcceptance(request, actor, start, ct);
        await Db.SaveChangesAsync(ct);
        return await Map(request, false, ct);
    }

    // Caller holds the tenant work reservation; all documents and notifications commit together.
    private async Task MaterializeAcceptance(PartnerCleaningRequest request, Guid actor, DateTime? plannedStart, CancellationToken ct, bool automatic = false)
    {
        await Staff(actor, ct);
        var company = await ActiveCompany(request.CompanyId, ct);
        var branch = await Branch(request.BranchId, request.CompanyId, ct);
        await EnsureUnstartedUnbilled(request, ct);
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        // A fixed start occupies the visit duration; a flexible visit occupies every day of the requested window.
        var start = plannedStart ?? TenantTimeZone.StartOfLocalDay(request.FromDate.ToDateTime(TimeOnly.MinValue), zone);
        var end = plannedStart.HasValue ? start.AddMinutes(request.EstimatedDurationMinutes)
            : TenantTimeZone.StartOfLocalDay(request.ToDate.ToDateTime(TimeOnly.MinValue).AddDays(1), zone);
        var localFrom = TenantTimeZone.LocalDate(start, zone);
        var localUntil = TenantTimeZone.LocalDate(end.AddTicks(-1), zone);
        // The visit carries the photo sections and general instructions in force when the partner's request is accepted.
        var portalSettings = await Db.LoadAsync<PartnerPortalSettings>(request.CompanyId, ct);
        var visitNotes = VisitNotes(portalSettings?.GeneralInstructions, request.Notes);
        // A request accepted before branch main shifts existed owns a whole shift and keeps it. Every other
        // request becomes one visit on its branch's main shift.
        var ownShift = request.LinkedShiftId is { } linkedId && !request.OccurrenceStartUtc.HasValue ? await Db.LoadAsync<Shift>(linkedId, ct) : null;
        if (ownShift is not { IsBranchMain: false } || ownShift.PartnerRequestId != request.Id)
        {
            await MaterializeOnBranchMain(request, actor, plannedStart, start, end, zone, company, branch, portalSettings, visitNotes, automatic, ct);
            return;
        }
        var shiftId = request.LinkedShiftId ?? PartnerCleaningRequest.StableId($"partner-shift:{request.Id}");
        var shift = await Db.LoadAsync<Shift>(shiftId, ct) ?? Shift.Create(request.CompanyId, request.BranchId,
            ShiftNaming.Build(branch), new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, visitNotes, localFrom, localUntil);
        shift.Id = shiftId; shift.PartnerRequestId = request.Id; shift.IsActive = true;
        shift.ValidFrom = localFrom; shift.ValidUntil = localUntil; shift.Notes = visitNotes;
        // Companies with quality cycle enabled give every accepted visit the checklist; an admin's later choice on the shift is kept.
        if (company.IsQualityCycleEnabled && company.QualityCycleTemplateId is Guid qualityTemplateId && shift.QualityCycleTemplateId is null)
        {
            shift.EnsureQualityCycleHistory();
            shift.QualityCycleTemplateId = qualityTemplateId;
            shift.RecordQualityCyclePlan(DateTime.UtcNow);
        }
        shift.RequiredPhotoLabels = portalSettings?.PhotoLabels.ToList() ?? [];
        shift.PartnerAccessInstructions = portalSettings?.Account.AccessInstructions;
        shift.PartnerContactName = portalSettings?.Account.ContactName;
        shift.PartnerContactPhone = portalSettings?.Account.ContactPhone;
        var occurrenceId = PartnerCleaningRequest.StableId($"partner-occurrence:{request.Id}");
        var occurrence = await Db.LoadAsync<ShiftOccurrenceException>(occurrenceId, ct)
            ?? ShiftOccurrenceException.Create(shiftId, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "Accepted partner cleaning", actor);
        occurrence.Id = occurrenceId;
        occurrence.Update(shiftId, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "Accepted partner cleaning", actor);
        var roster = ShiftVisitRoster.Resolve(shiftId, [], [occurrence], [], []);
        await VisitAssignmentSafety.CheckConflicts(shifts, shift, roster, zone, ct);
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
        request.PlannedStartUtc = plannedStart; request.PlannedEndUtc = plannedStart.HasValue ? end : null;
        request.FirstAcceptedAtUtc ??= DateTime.UtcNow; request.Status = "Accepted"; Touch(request);
        // A single Marten commit owns every document. Repository methods that commit individually are deliberately avoided.
        await ShiftTabs.StoreAsync(Db, shift, ct); Db.Store(occurrence); Db.Store(assignment); Db.Store(billing); Db.Store(request);
        if (push is not null) await push.StageScheduleChangeAsync(Db, shiftId, assignments.Select(x => x.EmployeeId).Append(actor).Where(id => !automatic || id != actor), DateTimeOffset.UtcNow, ct);
        NotifyRequest(request, "RequestAccepted", "Company"); NotifyRequest(request, "RequestAccepted", "Staff");
    }

    /// <summary>
    /// Writes the accepted request as one visit on its branch's main shift, creating that shift on first use.
    /// The caller holds the tenant work reservation, so two acceptances cannot create two main shifts.
    /// </summary>
    private async Task MaterializeOnBranchMain(PartnerCleaningRequest request, Guid actor, DateTime? plannedStart, DateTime start, DateTime end,
        TimeZoneInfo zone, Company company, Branch branch, PartnerPortalSettings? portalSettings, string? visitNotes, bool automatic, CancellationToken ct)
    {
        var shift = await BranchMainShifts.FindOrCreateAsync(Db, branch, TenantTimeZone.LocalDate(start, zone), ct);
        // Companies with quality cycle enabled give every accepted visit the checklist; an admin's later choice on the shift is kept.
        if (company.IsQualityCycleEnabled && company.QualityCycleTemplateId is Guid qualityTemplateId && shift.QualityCycleTemplateId is null)
        {
            shift.EnsureQualityCycleHistory();
            shift.QualityCycleTemplateId = qualityTemplateId;
            shift.RecordQualityCyclePlan(DateTime.UtcNow);
            shift.CoverVisitDate(TenantTimeZone.LocalDate(start, zone));
        }
        // Company access and contact details belong to the main shift; notes and photo sections belong to the visit.
        shift.PartnerAccessInstructions = portalSettings?.Account.AccessInstructions;
        shift.PartnerContactName = portalSettings?.Account.ContactName;
        shift.PartnerContactPhone = portalSettings?.Account.ContactPhone;
        var occurrenceId = PartnerCleaningRequest.StableId($"partner-occurrence:{request.Id}");
        var others = await Db.Query<ShiftOccurrenceException>().Where(x => x.ShiftId == shift.Id).ToListAsync(ct);
        // A colliding start moves the visit without shortening it; the booking's own planned times stay as booked.
        var bookedEnd = end;
        var freeStart = BranchMainShifts.FreeStart(start, end, others.Where(x => x.Id != occurrenceId));
        end = end.Add(freeStart - start); start = freeStart;
        var occurrence = others.FirstOrDefault(x => x.Id == occurrenceId)
            ?? ShiftOccurrenceException.Create(shift.Id, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "Accepted partner cleaning", actor);
        occurrence.Id = occurrenceId;
        occurrence.Update(shift.Id, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "Accepted partner cleaning", actor);
        request.OccurrenceSequence ??= shift.NextOccurrenceSequence();
        occurrence.PartnerRequestId = request.Id; occurrence.Sequence = request.OccurrenceSequence;
        occurrence.Source = ShiftOccurrenceException.SourcePartnerPortal; occurrence.Notes = visitNotes;
        occurrence.CleaningType = request.CleaningType ?? CleaningTypes.Normal;
        occurrence.RequiredPhotoLabels = portalSettings?.PhotoLabels.ToList() ?? [];
        var roster = ShiftVisitRoster.Resolve(shift.Id, [], [occurrence], [], []);
        await VisitAssignmentSafety.CheckConflicts(shifts, shift, roster, zone, ct);
        // An earlier hand-made roster change for this visit would hide the cleaner who has just accepted it.
        Db.Delete<ShiftOccurrenceAssignment>(ShiftOccurrenceAssignment.Identity(shift.Id, null, occurrenceId));
        var previousEmployee = request.AcceptedEmployeeId;
        request.LinkedShiftId = shift.Id; request.OccurrenceStartUtc = start; request.AcceptedEmployeeId = actor;
        request.PlannedStartUtc = plannedStart; request.PlannedEndUtc = plannedStart.HasValue ? bookedEnd : null;
        request.FirstAcceptedAtUtc ??= DateTime.UtcNow; request.Status = "Accepted"; Touch(request);
        // A single Marten commit owns every document. Repository methods that commit individually are deliberately avoided.
        await ShiftTabs.StoreAsync(Db, shift, ct); Db.Store(occurrence); Db.Store(request);
        if (push is not null) await push.StageScheduleChangeAsync(Db, shift.Id,
            new[] { previousEmployee, actor }.OfType<Guid>().Distinct().Where(id => !automatic || id != actor), DateTimeOffset.UtcNow, ct);
        NotifyRequest(request, "RequestAccepted", "Company"); NotifyRequest(request, "RequestAccepted", "Staff");
    }

    /// <summary>
    /// Takes an accepted request's visit off the schedule before it is offered again, moved or cancelled.
    /// A visit on a branch main shift is removed on its own; the main shift and its other visits are untouched.
    /// A request that owns a whole shift (accepted before main shifts existed) deactivates that shift, as it always has.
    /// </summary>
    private async Task ReleaseVisit(PartnerCleaningRequest request, bool notifyAccepted, bool removeOccurrence, CancellationToken ct)
    {
        if (!request.LinkedShiftId.HasValue) return;
        var shiftId = request.LinkedShiftId.Value;
        if (request.OccurrenceStartUtc.HasValue)
        {
            if (push is not null && notifyAccepted && request.AcceptedEmployeeId.HasValue)
                await push.StageScheduleChangeAsync(Db, shiftId, [request.AcceptedEmployeeId.Value], DateTimeOffset.UtcNow, ct);
            if (!removeOccurrence) return;
            var occurrenceId = PartnerCleaningRequest.StableId($"partner-occurrence:{request.Id}");
            Db.Delete<ShiftOccurrenceException>(occurrenceId);
            Db.Delete<ShiftOccurrenceAssignment>(ShiftOccurrenceAssignment.Identity(shiftId, null, occurrenceId));
            request.LinkedShiftId = null; request.OccurrenceStartUtc = null;
            return;
        }
        var shift = await Db.LoadAsync<Shift>(shiftId, ct);
        if (shift is null) return;
        shift.Deactivate(); await ShiftTabs.StoreAsync(Db, shift, ct); await ReleaseShiftAssignments(shift.Id, ct);
        if (push is not null && notifyAccepted && request.AcceptedEmployeeId.HasValue)
            await push.StageScheduleChangeAsync(Db, shift.Id, [request.AcceptedEmployeeId.Value], DateTimeOffset.UtcNow, ct);
    }

    public async Task Decline(Guid actor, Guid offerId, PartnerDecline body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        await Staff(actor, ct);
        var request = await Db.Query<PartnerCleaningRequest>().FirstOrDefaultAsync(x => x.OfferId == offerId && x.OfferedEmployeeId == actor, ct)
            ?? throw new NotFoundException("Cleaning offer", offerId);
        // Status first: an offer that was already accepted or cancelled also has a newer version, and the user
        // should hear that it is no longer waiting for an answer, not that someone else changed it.
        if (request.Status != "Offered") throw new ShiftConflictException("This offer is no longer waiting for your answer, so it cannot be declined. Check your scheduled work for its current status.");
        CheckVersion(request, body.Version);
        if (body.Reason?.Length > 1000) throw new ValidationException("Use at most 1000 characters for the reason.");
        InvalidateOffer(request); request.Status = "Requested"; Touch(request); Db.Store(request);
        NotifyRequest(request, "OfferDeclined", "Staff"); NotifyRequest(request, "AwaitingAssignment", "Company");
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

    /// <summary>The company's photo sections and general instructions, from its one-time portal setup.</summary>
    public async Task<PartnerSettingsDto> Settings(Guid actor, CancellationToken ct)
    {
        var (_, company) = await Partner(actor, ct);
        var settings = await Db.LoadAsync<PartnerPortalSettings>(company.Id, ct);
        return new(settings?.PhotoLabels.ToArray() ?? [], settings?.Account ?? new(), settings?.GeneralInstructions);
    }

    /// <summary>
    /// The instructions the cleaner reads for a visit: the company's general instructions first, then the
    /// instructions written for this visit. Either part may be empty.
    /// </summary>
    public static string? VisitNotes(string? generalInstructions, string? visitInstructions)
    {
        var parts = new[] { generalInstructions?.Trim(), visitInstructions?.Trim() }.Where(part => !string.IsNullOrEmpty(part));
        var joined = string.Join("\n\n", parts);
        return joined.Length == 0 ? null : joined;
    }

    // Trims blanks to null and enforces field limits. Billing, contact and access details share these rules.
    private static PartnerAccountDetails CleanAccount(PartnerAccountDetails? account)
    {
        account ??= new();
        static string? Clean(string? value, int max, string field)
        {
            var trimmed = value?.Trim();
            if (trimmed?.Length > max) throw new ValidationException($"Use at most {max} characters for {field}.");
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
        var email = Clean(account.ContactEmail, PartnerPortalSettings.MaxAccountFieldLength, "the contact email");
        if (email is not null && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new ValidationException("Enter a valid contact email.");
        return new PartnerAccountDetails
        {
            BillingName = Clean(account.BillingName, PartnerPortalSettings.MaxAccountFieldLength, "the billing name"),
            InvoiceAddress = Clean(account.InvoiceAddress, PartnerPortalSettings.MaxAccountFieldLength, "the invoice address"),
            BusinessId = Clean(account.BusinessId, PartnerPortalSettings.MaxAccountFieldLength, "the business ID"),
            InvoiceReference = Clean(account.InvoiceReference, PartnerPortalSettings.MaxAccountFieldLength, "the invoice reference"),
            ContactName = Clean(account.ContactName, PartnerPortalSettings.MaxAccountFieldLength, "the contact name"),
            ContactPhone = Clean(account.ContactPhone, PartnerPortalSettings.MaxAccountFieldLength, "the contact phone"),
            ContactEmail = email,
            AccessInstructions = Clean(account.AccessInstructions, PartnerPortalSettings.MaxAccessInstructionsLength, "the access instructions"),
        };
    }

    /// <summary>Replace the company's photo sections. Visits accepted from now on require them.</summary>
    public async Task<PartnerSettingsDto> UpdateSettings(Guid actor, PartnerSettingsInput body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        var labels = (body.PhotoLabels ?? []).Select(label => (label ?? "").Trim()).ToList();
        if (labels.Count > PartnerPortalSettings.MaxPhotoLabels) throw new ValidationException($"Use at most {PartnerPortalSettings.MaxPhotoLabels} photo sections.");
        if (labels.Any(label => label.Length == 0)) throw new ValidationException("Each photo section needs a name.");
        if (labels.Any(label => label.Length > PartnerPortalSettings.MaxPhotoLabelLength)) throw new ValidationException($"Use at most {PartnerPortalSettings.MaxPhotoLabelLength} characters for a photo section name.");
        if (labels.Select(label => label.ToLowerInvariant()).Distinct().Count() != labels.Count) throw new ValidationException("Each photo section can only be listed once.");
        var general = body.GeneralInstructions?.Trim();
        if (general?.Length > PartnerPortalSettings.MaxGeneralInstructionsLength) throw new ValidationException($"Use at most {PartnerPortalSettings.MaxGeneralInstructionsLength} characters for the general instructions.");
        var settings = await Db.LoadAsync<PartnerPortalSettings>(company.Id, ct) ?? new PartnerPortalSettings { Id = company.Id };
        // The portal no longer edits account details. A save without them keeps what is stored.
        var account = body.Account is null ? settings.Account : CleanAccount(body.Account);
        settings.PhotoLabels = labels; settings.Account = account; settings.GeneralInstructions = string.IsNullOrEmpty(general) ? null : general;
        settings.UpdatedUtc = DateTime.UtcNow;
        Db.Store(settings);
        await Db.SaveChangesAsync(ct);
        return new(labels, account, settings.GeneralInstructions);
    }

    private static readonly string[] RequestChangeActions = ["Message", "Reschedule", "Instructions", "Cancel"];

    /// <summary>
    /// Owner asks for a change to one cleaning request. Reschedule, Instructions and Cancel are structured so staff
    /// can apply them from the change list; Message only needs a reply.
    /// </summary>
    public async Task<PartnerChangeDto> RequestChange(Guid actor, Guid id, PartnerRequestChange body, CancellationToken ct)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var (_, company) = await Partner(actor, ct);
        ValidateText(body.Message, 2000, "Change request");
        var action = string.IsNullOrWhiteSpace(body.Action) ? "Message" : body.Action.Trim();
        if (!RequestChangeActions.Contains(action)) throw new ValidationException("Choose what you need changed.");
        // A cancellation reason is stored on the request, which allows 1000 characters.
        if (action == "Cancel" && body.Message.Trim().Length > 1000) throw new ValidationException("Use at most 1000 characters for the cancellation reason.");
        var request = await Request(id, company.Id, ct);
        if (request.Status == PartnerCleaningRequest.StatusCancelled) throw new ShiftConflictException("This request is cancelled.");
        var change = new PartnerChangeRequest { CompanyId = company.Id, BranchId = request.BranchId, RequestId = request.Id,
            RequestedByEmployeeId = actor, Kind = "Cleaning", Message = body.Message.Trim(), Action = action };
        switch (action)
        {
            case "Reschedule":
                if (body.FromDate is not { } from || body.ToDate is not { } to) throw new ValidationException("Choose the new dates for the cleaning.");
                var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
                ValidateWindow(from, to, body.ExactStartLocalTime, request.EstimatedDurationMinutes, zone);
                change.ProposedFromDate = from; change.ProposedToDate = to;
                change.ProposedExactStartLocalTime = string.IsNullOrWhiteSpace(body.ExactStartLocalTime) ? null : body.ExactStartLocalTime;
                break;
            case "Instructions":
                if (string.IsNullOrWhiteSpace(body.Notes)) throw new ValidationException("Write the new instructions for the cleaner.");
                if (body.Notes.Trim().Length > 4000) throw new ValidationException("Use at most 4000 characters for cleaning instructions.");
                change.ProposedNotes = body.Notes.Trim();
                break;
        }
        Db.Store(change); Notify(company.Id, request.BranchId, request.Id, "ChangeRequested", 1, "Staff", null, change.Id);
        await Db.SaveChangesAsync(ct);
        return MapChange(change);
    }

    /// <summary>
    /// Staff apply a structured request at the request version they reviewed. The operation uses the same rules as
    /// the cancel and edit screens, then the change is resolved with the staff's note.
    /// </summary>
    public async Task<PartnerChangeDto> Apply(Guid actor, Guid id, PartnerChangeApply body, CancellationToken ct)
    {
        await RequireManager(actor, ct);
        ValidateText(body.Resolution, 2000, "Resolution");
        var change = await Db.LoadAsync<PartnerChangeRequest>(id, ct) ?? throw new NotFoundException("Change request", id);
        if (change.ResolvedUtc.HasValue) throw new ShiftConflictException("This change request has already been resolved.");
        if (!change.RequestId.HasValue || change.Action == "Message") throw new ValidationException("This change only needs a reply. Use Resolve instead.");
        var requestId = change.RequestId.Value;
        switch (change.Action)
        {
            case "Cancel":
                await Cancel(actor, requestId, body.Version, true, change.Message, ct);
                break;
            case "Reschedule":
                var current = await Request(requestId, null, ct);
                await StaffEdit(actor, requestId, new EditPartnerRequest(body.Version, change.ProposedFromDate!.Value, change.ProposedToDate!.Value,
                    change.ProposedExactStartLocalTime, current.Notes), ct);
                break;
            case "Instructions":
                var target = await Request(requestId, null, ct);
                await StaffEdit(actor, requestId, new EditPartnerRequest(body.Version, target.FromDate, target.ToDate, target.ExactStartLocalTime,
                    change.ProposedNotes), ct);
                break;
        }
        change.ResolvedUtc = DateTime.UtcNow; change.Resolution = body.Resolution.Trim();
        Db.Store(change); Notify(change.CompanyId, change.BranchId, change.RequestId, "ChangeResolved", 1, "Company", null, change.Id);
        await Db.SaveChangesAsync(ct);
        return MapChange(change);
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
        var invoices = await Db.Query<Invoice>().Where(x => x.CompanyId == request.CompanyId).ToListAsync(ct);
        if (request.OccurrenceStartUtc is { } visitStart)
        {
            // A branch main shift holds many visits: only this request's own visit decides.
            var forms = await Db.Query<QualityCycleForm>().Where(x => x.ShiftId == id).ToListAsync(ct);
            // A visit merged in from a shift of its own was invoiced under that shift's id.
            var legacyId = (await Db.LoadAsync<ShiftOccurrenceException>(PartnerCleaningRequest.StableId($"partner-occurrence:{request.Id}"), ct))?.LegacyShiftId;
            var lockReason = BranchMainShifts.VisitLockReason(forms, invoices, id, visitStart)
                ?? (legacyId is { } old ? BranchMainShifts.VisitLockReason(await Db.Query<QualityCycleForm>().Where(x => x.ShiftId == old).ToListAsync(ct), invoices, old, visitStart) : null);
            if (lockReason is not null)
                throw new ShiftConflictException($"{lockReason}. This visit cannot be reassigned, moved or cancelled.");
            return;
        }
        if (await Db.Query<QualityCycleForm>().AnyAsync(x => x.ShiftId == id && x.ShiftDeletedUtc == null && (x.StartedAtUtc != null || x.IsSubmitted), ct))
            throw new ShiftConflictException("Work has started. Use a separate staff correction process; this visit cannot be reassigned, moved or cancelled.");
        if (invoices.Any(x => x.Status is "Issued" or "Paid" && x.Lines.Any(l => l.ShiftId == id)))
            throw new ShiftConflictException("This visit has already been invoiced and cannot be changed.");
    }

    private static string CleaningOf(string? value)
    {
        try { return CleaningTypes.Normalize(value); }
        catch (ArgumentException ex) { throw new ValidationException([new FluentValidation.Results.ValidationFailure("CleaningType", ex.Message)]); }
    }

    private async Task SetWindow(PartnerCleaningRequest request, DateOnly from, DateOnly to, string? exact, string? notes, CancellationToken ct)
    {
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        ValidateWindow(from, to, exact, request.EstimatedDurationMinutes, zone);
        if (notes?.Length > 4000) throw new ValidationException("Use at most 4000 characters for cleaning instructions.");
        request.FromDate = from; request.ToDate = to; request.ExactStartLocalTime = string.IsNullOrWhiteSpace(exact) ? null : exact;
        request.Notes = notes?.Trim();
        // The cleaner sees the shift's notes, so an edited request keeps its linked shift's instructions current.
        if (request.OccurrenceStartUtc.HasValue)
        {
            // On a branch main shift the instructions belong to this request's visit, not to the shared shift.
            if (await Db.LoadAsync<ShiftOccurrenceException>(PartnerCleaningRequest.StableId($"partner-occurrence:{request.Id}"), ct) is { } visit)
            {
                visit.Notes = VisitNotes((await Db.LoadAsync<PartnerPortalSettings>(request.CompanyId, ct))?.GeneralInstructions, request.Notes);
                Db.Store(visit);
            }
        }
        else if (request.LinkedShiftId is { } shiftId && await Db.LoadAsync<Shift>(shiftId, ct) is { } shift)
        {
            var general = (await Db.LoadAsync<PartnerPortalSettings>(request.CompanyId, ct))?.GeneralInstructions;
            shift.Notes = VisitNotes(general, request.Notes);
            Db.Store(shift);
        }
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
        var forms = await VisitForms(item, ct);
        var started = forms.Where(x => x.StartedAtUtc.HasValue).Select(x => x.StartedAtUtc).Min();
        var ended = forms.Where(x => x.IsSubmitted && x.EndedAtUtc.HasValue).Select(x => x.EndedAtUtc).Max();
        var status = item.Status;
        if (status == "Accepted" && started.HasValue) status = forms.Count > 0 && forms.All(x => x.IsSubmitted) ? "Completed" : "InProgress";
        return new(item.Id, item.Revision, item.CompanyId, item.BranchId, branch?.Name ?? "Property", item.FromDate,
            item.ToDate, item.ExactStartLocalTime, item.Notes, item.PriceNet, item.VatRatePercent,
            item.EstimatedDurationMinutes, status, item.FirstAcceptedAtUtc, item.LinkedShiftId, item.PlannedStartUtc,
            item.PlannedEndUtc, employee is null ? null : staff ? $"{employee.FirstName} {employee.LastName}".Trim() : null, started,
            status == "Completed" ? ended : null, staff ? item.OfferedEmployeeId : null, staff ? item.OfferId : null,
            item.CleaningType ?? CleaningTypes.Normal);
    }

    // Quality Cycle checklists recorded for the accepted cleaner's linked visit occurrence.
    private async Task<List<QualityCycleForm>> VisitForms(PartnerCleaningRequest item, CancellationToken ct)
    {
        if (!item.LinkedShiftId.HasValue) return [];
        var forms = await Db.Query<QualityCycleForm>().Where(x => x.ShiftId == item.LinkedShiftId.Value && x.ShiftDeletedUtc == null).ToListAsync(ct);
        // JSON timestamps use the existing repository's UTC convention. Filter the sole linked visit in memory
        // rather than binding a UTC DateTime to Marten's timestamp-without-time-zone JSON comparison.
        return forms.Where(x => x.EmployeeId == item.AcceptedEmployeeId && x.ShiftOccurrenceUtc == (item.OccurrenceStartUtc ?? item.PlannedStartUtc)).ToList();
    }

    /// <summary>Read-only completed-visit summary for the partner owning the request. Cleaner identity is masked.</summary>
    public async Task<PartnerCompletionDto> Completion(Guid actor, Guid id, CancellationToken ct)
    {
        var company = (await Partner(actor, ct)).Company;
        var request = await Request(id, company.Id, ct);
        var forms = await VisitForms(request, ct);
        var started = forms.Where(x => x.StartedAtUtc.HasValue).Select(x => x.StartedAtUtc).Min();
        var completed = request.Status == PartnerCleaningRequest.StatusAccepted && started.HasValue && forms.Count > 0 && forms.All(x => x.IsSubmitted);
        if (!completed) throw new ShiftConflictException("This visit has not been completed yet.");
        var branch = await Db.LoadAsync<Branch>(request.BranchId, ct);
        var messages = await Db.Query<PartnerChangeRequest>().Where(x => x.CompanyId == company.Id && x.RequestId == id).ToListAsync(ct);
        return new(request.Id, branch?.Name ?? "Property", request.FromDate, request.ToDate, request.PlannedStartUtc, request.PlannedEndUtc,
            started, forms.Where(x => x.EndedAtUtc.HasValue).Select(x => x.EndedAtUtc).Max(),
            forms.OrderBy(x => x.StartedAtUtc).Select(PartnerCompletionView.Report).ToArray(),
            messages.OrderByDescending(x => x.CreatedUtc).Select(MapChange).ToArray());
    }

    private static PartnerChangeDto MapChange(PartnerChangeRequest x) => new(x.Id, x.CompanyId, x.BranchId,
        x.RequestId, x.Kind, x.Message, x.CreatedUtc, x.ResolvedUtc, x.Resolution, x.Action,
        x.ProposedFromDate, x.ProposedToDate, x.ProposedExactStartLocalTime, x.ProposedNotes);
    // Shared by owner property creation and owner property edits, so both accept exactly the same details.
    private static void ValidateBranchDetails(CreatePartnerBranch body)
    {
        ValidateText(body.Name, 200, "Property name");
        if (body.ContactEmail?.Length > 254 || body.ContactEmail is { Length: > 0 } && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(body.ContactEmail))
            throw new ValidationException("Enter a valid property contact email.");
    }
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
        // Staff alerts are app push notifications (PartnerRequest intents), never email outbox rows.
        if (audience == "Staff") return;
        Db.Store(new PartnerNotification { Id = NoticeId(branch, request, kind, revision, audience, employee, eventId),
            CompanyId = company, BranchId = branch, RequestId = request, Kind = kind, Revision = revision,
            Audience = audience, RecipientEmployeeId = employee });
    }
    // A notice is keyed by its intent, so the same intent always maps to the same row.
    private static Guid NoticeId(Guid branch, Guid? request, string kind, int revision, string audience,
        Guid? employee, Guid? eventId = null) =>
        PartnerCleaningRequest.StableId($"notice:{eventId ?? request ?? branch}:{kind}:{revision}:{audience}:{employee}");
}
