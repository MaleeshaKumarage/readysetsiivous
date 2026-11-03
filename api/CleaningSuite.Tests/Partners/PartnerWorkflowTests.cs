using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using FluentValidation;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Partners;

public class PartnerWorkflowTests
{
    [Fact]
    public void Exact_appointment_rejects_multi_day_and_crosses_midnight()
    {
        var day = new DateOnly(2026, 11, 10);
        Assert.Throws<ValidationException>(() => PartnerService.ValidateWindow(day, day.AddDays(1), "08:00", 120, TimeZoneInfo.Utc));
        Assert.Throws<ValidationException>(() => PartnerService.ValidateWindow(day, day, "23:00", 120, TimeZoneInfo.Utc));
        PartnerService.ValidateWindow(day, day, "22:00", 120, TimeZoneInfo.Utc);
    }

    [Fact]
    public void Daylight_saving_nonexistent_and_ambiguous_times_are_explicit_errors()
    {
        var zone = TenantTimeZone.Resolve("Europe/Helsinki");
        Assert.Throws<ValidationException>(() => PartnerService.LocalToUtc(new(2026, 3, 29, 3, 30, 0), zone));
        Assert.Throws<ValidationException>(() => PartnerService.LocalToUtc(new(2026, 10, 25, 3, 30, 0), zone));
        Assert.Equal(new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc), PartnerService.LocalToUtc(new(2026, 11, 10, 8, 0, 0), zone));
    }

    [Fact]
    public void Linked_partner_shift_cannot_be_changed_through_generic_mutation_paths()
    {
        PartnerShiftGuard.EnsureOrdinary(new Shift());
        Assert.Throws<ShiftConflictException>(() => PartnerShiftGuard.EnsureOrdinary(new Shift { PartnerRequestId = Guid.NewGuid() }));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Concurrent_acceptance_materializes_exactly_one_shift_and_financial_snapshot()
    {
        using var fixture = await Fixture.Create();
        var request = await fixture.SubmitOffer();
        using var one = fixture.Session(); using var two = fixture.Session();
        var results = await Task.WhenAll(fixture.Service(one).Accept(fixture.Cleaner.Id, request.OfferId!.Value, new(request.Version), default),
            fixture.Service(two).Accept(fixture.Cleaner.Id, request.OfferId.Value, new(request.Version), default));
        Assert.Equal(results[0].LinkedShiftId, results[1].LinkedShiftId);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var shift = Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.Equal(request.Id, shift.PartnerRequestId);
        Assert.Single(await reader.Query<ShiftAssignment>().Where(x => x.IsActive).ToListAsync());
        Assert.Single(await reader.Query<ShiftOccurrenceException>().ToListAsync());
        var plan = Assert.Single(Assert.Single(await reader.Query<ShiftBillingProfile>().ToListAsync()).Plans);
        Assert.Equal(100m, plan.RateNet); Assert.Equal(25.5m, plan.VatRatePercent);
        Assert.Contains(await reader.Query<PartnerNotification>().ToListAsync(), x => x.Kind == "RequestAccepted" && x.Audience == "Company");
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Edited_offer_cannot_be_accepted_and_first_acceptance_locks_partner_forever()
    {
        using var fixture = await Fixture.Create();
        var offer = await fixture.SubmitOffer();
        using (var session = fixture.Session())
        {
            var edited = await fixture.Service(session).Edit(fixture.Partner.Id, offer.Id, new(offer.Version, Fixture.Day, Fixture.Day, "10:00"), default);
            Assert.Equal("Requested", edited.Status);
        }
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<CleaningSuite.Application.Common.NotFoundException>(() => fixture.Service(session).Accept(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version), default));
        PartnerRequestDto accepted;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var current = await service.Get(fixture.Partner.Id, offer.Id, default);
            var second = await service.Offer(fixture.Admin.Id, offer.Id, new(current.Version, fixture.Cleaner.Id), default);
            accepted = await service.Accept(fixture.Cleaner.Id, second.OfferId!.Value, new(second.Version), default);
        }
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).Edit(fixture.Partner.Id, offer.Id, new(accepted.Version, Fixture.Day, Fixture.Day), default));
        using (var session = fixture.Session())
        {
            var reoffer = await fixture.Service(session).Offer(fixture.Admin.Id, offer.Id, new(accepted.Version, fixture.Cleaner.Id), default);
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).Cancel(fixture.Partner.Id, offer.Id, reoffer.Version, false, null, default));
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Overlapping_acceptance_rolls_back_all_materialization()
    {
        using var fixture = await Fixture.Create();
        var first = await fixture.SubmitOffer();
        using (var session = fixture.Session()) await fixture.Service(session).Accept(fixture.Cleaner.Id, first.OfferId!.Value, new(first.Version), default);
        var second = await fixture.SubmitOffer();
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).Accept(fixture.Cleaner.Id, second.OfferId!.Value, new(second.Version), default));
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Single(await reader.Query<Shift>().ToListAsync());
        var request = await reader.LoadAsync<PartnerCleaningRequest>(second.Id);
        Assert.Null(request!.LinkedShiftId); Assert.Equal("Offered", request.Status);
        Assert.Single(await reader.Query<ShiftBillingProfile>().ToListAsync());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Company_isolation_and_disabled_company_are_enforced_from_current_database_state()
    {
        using var fixture = await Fixture.Create();
        var foreign = Company.Create("FOREIGN", "Another company"); foreign.IsPartnerEnabled = true;
        var branch = Branch.Create(foreign.Id, "Foreign property"); branch.SetPricing(500, 25.5m, 60);
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { writer.Store(foreign); writer.Store(branch); await writer.SaveChangesAsync(); }
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<CleaningSuite.Application.Common.NotFoundException>(() => fixture.Service(session).Create(fixture.Partner.Id,
                new(Guid.NewGuid(), branch.Id, Fixture.Day, Fixture.Day), default));
        using (var session = new TenantSessionFactory(fixture.Store, Fixture.Context("other-tenant")))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service(session).Me(fixture.Partner.Id, default));
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant))
        {
            writer.Store(new PartnerChangeRequest { CompanyId = fixture.Company.Id, BranchId = fixture.Branch.Id, Message = "Own request", Resolution = "Updated property", ResolvedUtc = DateTime.UtcNow });
            writer.Store(new PartnerChangeRequest { CompanyId = foreign.Id, BranchId = branch.Id, Message = "Foreign private request" });
            await writer.SaveChangesAsync();
        }
        using (var session = fixture.Session())
        {
            var own = Assert.Single(await fixture.Service(session).OwnChanges(fixture.Partner.Id, default));
            Assert.Equal("Own request", own.Message); Assert.Equal("Updated property", own.Resolution);
        }
        using (var writer = fixture.Store.DirtyTrackedSession(fixture.Tenant))
        { var company = await writer.LoadAsync<Company>(fixture.Company.Id); company!.IsPartnerEnabled = false; await writer.SaveChangesAsync(); }
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service(session).Branches(fixture.Partner.Id, false, null, default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Reassignment_uses_same_shift_and_price_and_started_work_cannot_be_changed()
    {
        using var fixture = await Fixture.Create(); var offer = await fixture.SubmitOffer();
        PartnerRequestDto accepted;
        using (var session = fixture.Session()) accepted = await fixture.Service(session).Accept(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version), default);
        var replacement = new Employee { Role = Employee.RoleManager, FirstName = "Replacement" };
        using (var writer = fixture.Store.DirtyTrackedSession(fixture.Tenant))
        {
            writer.Store(replacement); var branch = await writer.LoadAsync<Branch>(fixture.Branch.Id);
            branch!.SetPricing(300, 14, 60); await writer.SaveChangesAsync();
        }
        PartnerRequestDto changed;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var edited = await service.StaffEdit(fixture.Admin.Id, offer.Id, new(accepted.Version, Fixture.Day, Fixture.Day, "12:00"), default);
            var reoffer = await service.Offer(fixture.Admin.Id, offer.Id, new(edited.Version, replacement.Id), default);
            changed = await service.Accept(replacement.Id, reoffer.OfferId!.Value, new(reoffer.Version), default);
        }
        Assert.Equal(accepted.LinkedShiftId, changed.LinkedShiftId); Assert.Equal(100, changed.PriceNet);
        Assert.Equal(120, changed.EstimatedDurationMinutes);
        using (var reader = fixture.Store.QuerySession(fixture.Tenant))
        {
            Assert.Single(await reader.Query<Shift>().ToListAsync());
            var old = await reader.Query<ShiftAssignment>().SingleAsync(x => x.EmployeeId == fixture.Cleaner.Id);
            Assert.False(old.IsActive); Assert.DoesNotContain(old.Periods, x => x.ToUtc is null);
            Assert.Equal(replacement.Id, Assert.Single(await reader.Query<ShiftOccurrenceException>().ToListAsync()).EmployeeId);
        }
        var form = QualityCycleForm.Create(changed.LinkedShiftId!.Value, "Cleaning", replacement.Id, "Replacement", Guid.Empty,
            "Shift activities", changed.PlannedStartUtc!.Value, [], changed.PlannedEndUtc!.Value);
        form.Start();
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { writer.Store(form); await writer.SaveChangesAsync(); }
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            Assert.Equal("InProgress", (await service.Get(fixture.Partner.Id, offer.Id, default)).Status);
            await Assert.ThrowsAsync<ShiftConflictException>(() => service.Offer(fixture.Admin.Id, offer.Id, new(changed.Version, fixture.Cleaner.Id), default));
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Flexible_visit_requires_full_duration_in_window_and_local_midnight_is_visible()
    {
        using var fixture = await Fixture.Create(); PartnerRequestDto offer;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var request = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day), default);
            offer = await service.Offer(fixture.Admin.Id, request.Id, new(request.Version, fixture.Cleaner.Id), default);
        }
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<ValidationException>(() => fixture.Service(session).Accept(fixture.Cleaner.Id, offer.OfferId!.Value,
                new(offer.Version, $"{Fixture.Day:yyyy-MM-dd}T23:00"), default));
        PartnerRequestDto accepted;
        using (var session = fixture.Session()) accepted = await fixture.Service(session).Accept(fixture.Cleaner.Id, offer.OfferId!.Value,
            new(offer.Version, $"{Fixture.Day:yyyy-MM-dd}T00:15"), default);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var shift = await reader.LoadAsync<Shift>(accepted.LinkedShiftId!.Value);
        Assert.Equal(Fixture.Day.ToDateTime(TimeOnly.MinValue), shift!.ValidFrom);
        using var rosterSession = fixture.Session();
        var roster = await new ShiftRepository(rosterSession).ResolveRosterAsync(shift, accepted.PlannedStartUtc!.Value.AddHours(-1),
            accepted.PlannedEndUtc!.Value.AddHours(1), TenantTimeZone.Resolve("Europe/Helsinki"), default);
        Assert.Single(roster); Assert.Equal(fixture.Cleaner.Id, Assert.Single(roster[0].EmployeeIds));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Accept_versus_cancel_cannot_leave_a_cancelled_request_with_a_live_shift()
    {
        using var fixture = await Fixture.Create(); var offer = await fixture.SubmitOffer();
        using var a = fixture.Session(); using var b = fixture.Session();
        async Task<bool> Attempt(Func<Task> action) { try { await action(); return true; }
            catch (Exception ex) when (ex is ShiftConflictException or CleaningSuite.Application.Common.NotFoundException) { return false; } }
        var outcomes = await Task.WhenAll(Attempt(async () => { await fixture.Service(a).Accept(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version), default); }),
            Attempt(async () => { await fixture.Service(b).Cancel(fixture.Partner.Id, offer.Id, offer.Version, false, null, default); }));
        Assert.Single(outcomes.Where(x => x));
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var request = await reader.LoadAsync<PartnerCleaningRequest>(offer.Id);
        var shifts = await reader.Query<Shift>().ToListAsync();
        if (request!.Status == "Cancelled") Assert.Empty(shifts);
        else { Assert.Equal("Accepted", request.Status); Assert.True(Assert.Single(shifts).IsActive); }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Automatic_offer_is_atomic_and_stale_retry_cannot_duplicate_materialization()
    {
        using var fixture = await Fixture.Create();
        using (var session = fixture.Session()) await fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, true, default);
        var accepted = await fixture.SubmitOffer();
        Assert.Equal("Accepted", accepted.Status); Assert.NotNull(accepted.LinkedShiftId);
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).Offer(fixture.Admin.Id, accepted.Id,
                new(accepted.Version - 2, fixture.Cleaner.Id), default));
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.Single(await reader.Query<ShiftOccurrenceException>().ToListAsync());
        Assert.Single(await reader.Query<ShiftAssignment>().Where(x => x.IsActive).ToListAsync());
        Assert.Single(await reader.Query<ShiftBillingProfile>().ToListAsync());
        var notices = await reader.Query<PartnerNotification>().Where(x => x.RequestId == accepted.Id).ToListAsync();
        Assert.DoesNotContain(notices, x => x.Kind is "OfferCreated" or "AwaitingAcceptance");
        Assert.Single(notices.Where(x => x.Kind == "WorkAutoAccepted"));
        Assert.Equal(2, notices.Count(x => x.Kind == "RequestAccepted"));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Pending_offer_blocks_enabling_and_disabling_only_changes_future_offers()
    {
        using var fixture = await Fixture.Create(); var pending = await fixture.SubmitOffer();
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, true, default));
        using (var session = fixture.Session()) await fixture.Service(session).Accept(fixture.Cleaner.Id, pending.OfferId!.Value, new(pending.Version), default);
        using (var session = fixture.Session()) await fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, true, default);
        using (var session = fixture.Session()) await fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, false, default);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Equal("Accepted", (await reader.LoadAsync<PartnerCleaningRequest>(pending.Id))!.Status);
        Assert.False((await reader.LoadAsync<Employee>(fixture.Cleaner.Id))!.AutoAcceptWork);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Automatic_offer_requires_exact_time_and_overlap_failure_commits_nothing()
    {
        using var fixture = await Fixture.Create();
        using (var session = fixture.Session()) await fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, true, default);
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var request = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day), default);
            await Assert.ThrowsAsync<ShiftConflictException>(() => service.Offer(fixture.Admin.Id, request.Id, new(request.Version, fixture.Cleaner.Id), default));
        }
        var first = await fixture.SubmitOffer();
        await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.SubmitOffer());
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.Single(await reader.Query<ShiftAssignment>().Where(x => x.IsActive).ToListAsync());
        Assert.Single(await reader.Query<ShiftBillingProfile>().ToListAsync());
        Assert.Single((await reader.Query<PartnerCleaningRequest>().ToListAsync()).Where(x => x.Status == "Accepted"));
        Assert.DoesNotContain(await reader.Query<PartnerCleaningRequest>().ToListAsync(), x => x.Status == "Offered");
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Automatic_single_day_exact_appointment_rejects_a_different_clock_and_retains_fixed_fallback()
    {
        using var fixture = await Fixture.Create();
        using var session = fixture.Session(); var service = fixture.Service(session);
        await service.SetWorkPreferences(fixture.Cleaner.Id, true, default);
        var request = await service.Create(fixture.Partner.Id,
            new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
        await Assert.ThrowsAsync<ValidationException>(() => service.Offer(fixture.Admin.Id, request.Id,
            new(request.Version, fixture.Cleaner.Id, $"{Fixture.Day:yyyy-MM-dd}T09:00"), default));
        var accepted = await service.Offer(fixture.Admin.Id, request.Id, new(request.Version, fixture.Cleaner.Id), default);
        Assert.Equal("Accepted", accepted.Status);
        Assert.Equal(new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc), accepted.PlannedStartUtc);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Legacy_multiday_exact_clock_requires_an_explicit_date_before_automatic_acceptance()
    {
        using var fixture = await Fixture.Create();
        using (var session = fixture.Session()) await fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, true, default);
        // Legacy/imported records may contain a clock time on a flexible date window.
        var request = new PartnerCleaningRequest
        {
            CompanyId = fixture.Company.Id, BranchId = fixture.Branch.Id, RequestedByEmployeeId = fixture.Partner.Id,
            FromDate = Fixture.Day, ToDate = Fixture.Day.AddDays(1), ExactStartLocalTime = "08:00",
            EstimatedDurationMinutes = 120, PriceNet = 100, VatRatePercent = 25.5m
        };
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { writer.Store(request); await writer.SaveChangesAsync(); }
        var push = new Mock<CleaningSuite.Application.Notifications.IPushOutbox>(MockBehavior.Strict);
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session, push.Object).Offer(fixture.Admin.Id, request.Id,
                new(request.Revision, fixture.Cleaner.Id), default));
        push.VerifyNoOtherCalls();
        using (var reader = fixture.Store.QuerySession(fixture.Tenant))
        {
            var unchanged = await reader.LoadAsync<PartnerCleaningRequest>(request.Id);
            Assert.Equal("Requested", unchanged!.Status); Assert.Equal(request.Revision, unchanged.Revision); Assert.Null(unchanged.OfferId);
            Assert.Empty(await reader.Query<Shift>().ToListAsync());
            Assert.Empty(await reader.Query<ShiftAssignment>().ToListAsync());
            Assert.Empty(await reader.Query<ShiftOccurrenceException>().ToListAsync());
            Assert.Empty(await reader.Query<ShiftBillingProfile>().ToListAsync());
            Assert.Empty(await reader.Query<PartnerNotification>().ToListAsync());
        }
        using (var session = fixture.Session())
        {
            var accepted = await fixture.Service(session).Offer(fixture.Admin.Id, request.Id,
                new(request.Revision, fixture.Cleaner.Id, $"{Fixture.Day.AddDays(1):yyyy-MM-dd}T09:00"), default);
            Assert.Equal("Accepted", accepted.Status);
            Assert.Equal(new DateTime(2026, 11, 11, 7, 0, 0, DateTimeKind.Utc), accepted.PlannedStartUtc);
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Preference_enable_racing_offer_never_hides_a_pending_offer()
    {
        using var fixture = await Fixture.Create();
        using var session = fixture.Session(); var service = fixture.Service(session);
        var request = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
        using var one = fixture.Session(); using var two = fixture.Session();
        async Task Enable() { try { await fixture.Service(one).SetWorkPreferences(fixture.Cleaner.Id, true, default); } catch (ShiftConflictException) { } }
        await Task.WhenAll(Enable(), fixture.Service(two).Offer(fixture.Admin.Id, request.Id, new(request.Version, fixture.Cleaner.Id), default));
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var employee = await reader.LoadAsync<Employee>(fixture.Cleaner.Id);
        var current = await reader.LoadAsync<PartnerCleaningRequest>(request.Id);
        Assert.Equal(employee!.AutoAcceptWork ? "Accepted" : "Offered", current!.Status);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Preferences_are_own_active_work_role_and_tenant_bound()
    {
        using var fixture = await Fixture.Create();
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetWorkPreferences(fixture.Partner.Id, true, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetWorkPreferences(Guid.NewGuid(), true, default));
            await service.SetWorkPreferences(fixture.Cleaner.Id, true, default);
            Assert.False((await service.WorkPreferences(fixture.Admin.Id, default)).AutoAcceptWork);
        }
        using var foreign = new TenantSessionFactory(fixture.Store, Fixture.Context("foreign-" + Guid.NewGuid().ToString("N")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service(foreign).SetWorkPreferences(fixture.Cleaner.Id, true, default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Staff_property_and_quote_lists_exclude_non_partner_companies_and_orphan_branches()
    {
        using var fixture = await Fixture.Create();
        var regular = Company.Create("REGULAR", "Ordinary cleaning customer");
        var disabled = Company.Create("DISABLED", "Former partner");
        var inactivePartner = Company.Create("INACTIVE", "Inactive partner");
        inactivePartner.IsPartnerEnabled = true; inactivePartner.IsActive = false;
        var regularBranch = Branch.Create(regular.Id, "Ordinary property");
        var disabledBranch = Branch.Create(disabled.Id, "Former partner property");
        var inactiveBranch = Branch.Create(inactivePartner.Id, "Inactive partner history");
        var orphan = Branch.Create(Guid.NewGuid(), "Orphan property");
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant))
        {
            writer.Store(regular, disabled, inactivePartner);
            var quote = Branch.Create(fixture.Company.Id, "Partner quote");
            foreach (var branch in new[] { regularBranch, disabledBranch, inactiveBranch, orphan, quote })
            { branch.QuoteRequestedUtc = DateTime.UtcNow; writer.Store(branch); }
            await writer.SaveChangesAsync();
        }
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var branches = await service.Branches(fixture.Admin.Id, true, null, default);
            Assert.Contains(branches, x => x.Id == fixture.Branch.Id);
            Assert.Contains(branches, x => x.Id == inactiveBranch.Id);
            Assert.DoesNotContain(branches, x => x.Id == regularBranch.Id || x.Id == disabledBranch.Id || x.Id == orphan.Id);
            Assert.Empty(await service.Branches(fixture.Admin.Id, true, regular.Id, default));
            var quotes = await service.Quotes(fixture.Admin.Id, default);
            Assert.Equal("Partner quote", Assert.Single(quotes).Name);
        }
        using (var writer = fixture.Store.DirtyTrackedSession(fixture.Tenant))
        { var company = await writer.LoadAsync<Company>(fixture.Company.Id); company!.IsPartnerEnabled = false; await writer.SaveChangesAsync(); }
        using (var session = fixture.Session())
        {
            Assert.DoesNotContain(await fixture.Service(session).Branches(fixture.Admin.Id, true, null, default), x => x.CompanyId == fixture.Company.Id);
            Assert.Empty(await fixture.Service(session).Quotes(fixture.Admin.Id, default));
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Preferred_cleaner_receives_direct_offer_without_staff_broadcast_and_decline_falls_back()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(false);
        var clientId = Guid.NewGuid(); PartnerRequestDto created;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            created = await service.Create(fixture.Partner.Id, new(clientId, fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "08:00"), default);
            var retry = await service.Create(fixture.Partner.Id, new(clientId, fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "08:00"), default);
            Assert.Equal(created.Id, retry.Id); Assert.Equal("Offered", retry.Status);
        }
        using (var reader = fixture.Store.QuerySession(fixture.Tenant))
        {
            var notices = await reader.Query<PartnerNotification>().ToListAsync();
            Assert.DoesNotContain(notices, x => x.Audience == "Staff");
            Assert.Contains(notices, x => x.Kind == "OfferCreated" && x.RecipientEmployeeId == fixture.Cleaner.Id);
        }
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session); var offer = Assert.Single(await service.Offers(fixture.Cleaner.Id, default));
            await service.Decline(fixture.Cleaner.Id, offer.Id, new(offer.Version), default);
        }
        using var after = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Equal("Requested", (await after.LoadAsync<PartnerCleaningRequest>(created.Id))!.Status);
        Assert.Contains(await after.Query<PartnerNotification>().ToListAsync(), x => x.Kind == "OfferDeclined" && x.Audience == "Staff");
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Opted_in_cleaner_auto_accepts_exact_time_but_flexible_and_conflicting_requests_remain_offers()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(true);
        using (var session = fixture.Session())
        {
            var exact = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "08:00"), default);
            Assert.Equal("Accepted", exact.Status); Assert.NotNull(exact.LinkedShiftId);
        }
        using (var session = fixture.Session())
        {
            var conflict = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "09:00"), default);
            Assert.Equal("Offered", conflict.Status); Assert.Null(conflict.LinkedShiftId);
        }
        using (var session = fixture.Session())
        {
            var flexible = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay.AddDays(1), Fixture.FutureDay.AddDays(2)), default);
            Assert.Equal("Offered", flexible.Status); Assert.Null(flexible.LinkedShiftId);
        }
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.DoesNotContain(await reader.Query<PartnerNotification>().ToListAsync(), x => x.Audience == "Staff");
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Direct_cleaner_manual_approval_and_partner_cancellation_keep_staff_bypassed_after_branch_config_changes()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(false);
        PartnerRequestDto created;
        using (var session = fixture.Session()) created = await fixture.Service(session).Create(fixture.Partner.Id,
            new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "08:00"), default);
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var cancelled = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay.AddDays(1), Fixture.FutureDay.AddDays(1)), default);
            await service.Cancel(fixture.Partner.Id, cancelled.Id, cancelled.Version, false, null, default);
        }
        using (var writer = fixture.Store.DirtyTrackedSession(fixture.Tenant))
        { var branch = await writer.LoadAsync<Branch>(fixture.Branch.Id); branch!.PreferredCleanerEmployeeId = null; await writer.SaveChangesAsync(); }
        PartnerRequestDto accepted;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session); var offer = Assert.Single(await service.Offers(fixture.Cleaner.Id, default));
            accepted = await service.Accept(fixture.Cleaner.Id, offer.Id, new(offer.Version), default);
        }
        using (var session = fixture.Session())
            await fixture.Service(session).Cancel(fixture.Admin.Id, created.Id, accepted.Version, true, "Staff cancellation", default);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.DoesNotContain(await reader.Query<PartnerNotification>().ToListAsync(), x => x.Audience == "Staff");
        Assert.True((await reader.LoadAsync<PartnerCleaningRequest>(created.Id))!.DirectlyRouted);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Concurrent_direct_auto_accept_create_is_idempotent()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(true);
        var body = new CreatePartnerRequest(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "08:00");
        using var first = fixture.Session(); using var second = fixture.Session();
        var push = new Mock<CleaningSuite.Application.Notifications.IPushOutbox>();
        var results = await Task.WhenAll(fixture.Service(first, push.Object).Create(fixture.Partner.Id, body, default), fixture.Service(second, push.Object).Create(fixture.Partner.Id, body, default));
        push.Verify(x => x.StageAsync(It.IsAny<IDocumentSession>(),
            It.Is<CleaningSuite.Domain.Notifications.PushIntent>(i => i.Kind == "WorkAutoAccepted" && i.EmployeeId == fixture.Cleaner.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        push.Verify(x => x.StageScheduleChangeAsync(It.IsAny<IDocumentSession>(), It.IsAny<Guid>(),
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(fixture.Cleaner.Id)), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        push.Verify(x => x.StagePartnerRequestAsync(It.IsAny<IDocumentSession>(), It.IsAny<Guid>(), It.IsAny<int>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.All(results, x => Assert.Equal("Accepted", x.Status));
        Assert.Equal(results[0].LinkedShiftId, results[1].LinkedShiftId);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Single(await reader.Query<PartnerCleaningRequest>().ToListAsync());
        Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.Single(await reader.Query<ShiftAssignment>().ToListAsync());
        Assert.Single(await reader.Query<PartnerNotification>().Where(x => x.Kind == "RequestAccepted" && x.Audience == "Company").ToListAsync());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Conflicting_direct_auto_attempt_preserves_inactive_linked_history_in_dirty_tracked_session()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(true);
        PartnerRequestDto target;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var blocker = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "08:00"), default);
            Assert.Equal("Accepted", blocker.Status);
            target = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay.AddDays(1), Fixture.FutureDay.AddDays(1)), default);
        }
        var shiftId = PartnerCleaningRequest.StableId($"partner-shift:{target.Id}");
        var occurrenceId = PartnerCleaningRequest.StableId($"partner-occurrence:{target.Id}");
        using (var writer = fixture.Store.DirtyTrackedSession(fixture.Tenant))
        {
            // Legacy unaccepted request with inactive linked scheduling history.
            var request = await writer.LoadAsync<PartnerCleaningRequest>(target.Id);
            request!.LinkedShiftId = shiftId; request.Status = "Requested";
            request.OfferId = null; request.OfferedEmployeeId = null; request.DirectlyRouted = false;
            var historical = Shift.Create(fixture.Company.Id, fixture.Branch.Id, "Preserved historical title",
                new() { Type = ShiftScheduleType.OnCallFlexible }, "Historical notes", Fixture.FutureDay.AddDays(1).ToDateTime(TimeOnly.MinValue), null);
            historical.Id = shiftId; historical.PartnerRequestId = target.Id; historical.Deactivate();
            var oldStart = Fixture.FutureDay.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
            var occurrence = ShiftOccurrenceException.Create(shiftId, null, oldStart, oldStart.AddHours(2),
                ShiftOccurrenceExceptionStatus.Extra, "Historical schedule reason", fixture.Cleaner.Id);
            occurrence.Id = occurrenceId;
            writer.Store(historical); writer.Store(occurrence); await writer.SaveChangesAsync();
        }
        string beforeShift, beforeOccurrence;
        using (var reader = fixture.Store.QuerySession(fixture.Tenant))
        {
            beforeShift = System.Text.Json.JsonSerializer.Serialize(await reader.LoadAsync<Shift>(shiftId));
            beforeOccurrence = System.Text.Json.JsonSerializer.Serialize(await reader.LoadAsync<ShiftOccurrenceException>(occurrenceId));
        }
        using (var session = fixture.Session())
        {
            var changed = await fixture.Service(session).Edit(fixture.Partner.Id, target.Id,
                new(target.Version, Fixture.FutureDay, Fixture.FutureDay, "09:00", "New requested instructions"), default);
            Assert.Equal("Offered", changed.Status);
        }
        using var after = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Equal(beforeShift, System.Text.Json.JsonSerializer.Serialize(await after.LoadAsync<Shift>(shiftId)));
        Assert.Equal(beforeOccurrence, System.Text.Json.JsonSerializer.Serialize(await after.LoadAsync<ShiftOccurrenceException>(occurrenceId)));
        Assert.False((await after.LoadAsync<Shift>(shiftId))!.IsActive);
        Assert.Null((await after.LoadAsync<PartnerCleaningRequest>(target.Id))!.AcceptedEmployeeId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Repeated_direct_decline_fallbacks_stage_distinct_staff_revisions_and_clear_provenance()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(false);
        var push = new Mock<CleaningSuite.Application.Notifications.IPushOutbox>();
        using var session = fixture.Session(); var service = fixture.Service(session, push.Object);
        var created = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay), default);
        var offer = Assert.Single(await service.Offers(fixture.Cleaner.Id, default));
        await service.Decline(fixture.Cleaner.Id, offer.Id, new(offer.Version), default);
        var changed = await service.Get(fixture.Partner.Id, created.Id, default);
        var next = await service.Offer(fixture.Admin.Id, created.Id, new(changed.Version, fixture.Cleaner.Id), default);
        await service.Decline(fixture.Cleaner.Id, next.OfferId!.Value, new(next.Version), default);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var current = await reader.LoadAsync<PartnerCleaningRequest>(created.Id);
        Assert.False(current!.DirectlyRouted); Assert.Equal("Requested", current.Status);
        push.Verify(x => x.StagePartnerRequestAsync(It.IsAny<IDocumentSession>(), created.Id, offer.Version + 1,
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        push.Verify(x => x.StagePartnerRequestAsync(It.IsAny<IDocumentSession>(), created.Id, next.Version + 1,
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, (await reader.Query<PartnerNotification>().Where(x => x.Kind == "OfferDeclined" && x.Audience == "Staff").ToListAsync()).Count);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Invalid_preferred_cleaner_falls_back_without_changing_saved_branch_or_historical_requests()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(false);
        using (var writer = fixture.Store.DirtyTrackedSession(fixture.Tenant))
        { var cleaner = await writer.LoadAsync<Employee>(fixture.Cleaner.Id); cleaner!.IdentitySyncPending = true; await writer.SaveChangesAsync(); }
        using (var session = fixture.Session())
        {
            var request = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.FutureDay, Fixture.FutureDay, "08:00"), default);
            Assert.Equal("Requested", request.Status);
        }
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Contains(await reader.Query<PartnerNotification>().ToListAsync(), x => x.Kind == "PreferredCleanerUnavailable" && x.Audience == "Staff");
        Assert.Equal(fixture.Cleaner.Id, (await reader.LoadAsync<Branch>(fixture.Branch.Id))!.PreferredCleanerEmployeeId);
    }

    private sealed class Fixture : IDisposable
    {
        public static DateOnly FutureDay => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));
        public static DateOnly Day => new(2026, 11, 10);
        private readonly ServiceProvider provider;
        public IDocumentStore Store { get; }
        public string Tenant { get; } = "partner-test-" + Guid.NewGuid().ToString("N");
        public Company Company { get; } = Company.Create(Guid.NewGuid().ToString(), "Owner company");
        public Branch Branch { get; private set; } = null!;
        public Employee Partner { get; private set; } = null!;
        public Employee Admin { get; } = new() { Role = Employee.RoleAdmin, KeycloakUserId = "synthetic-admin" };
        public Employee Cleaner { get; } = new() { Role = Employee.RoleEmployee, KeycloakUserId = "synthetic-cleaner", FirstName = "Cleaner", LastName = "Test" };
        private Fixture(ServiceProvider provider) { this.provider = provider; Store = provider.GetRequiredService<IDocumentStore>(); }
        public static async Task<Fixture> Create()
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "partner_" + Guid.NewGuid().ToString("N"));
            var fixture = new Fixture(services.BuildServiceProvider());
            await fixture.Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            fixture.Company.IsPartnerEnabled = true;
            fixture.Branch = Branch.Create(fixture.Company.Id, "Apartment"); fixture.Branch.SetPricing(100, 25.5m, 120);
            fixture.Partner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = fixture.Company.Id };
            using var writer = fixture.Store.LightweightSession(fixture.Tenant);
            writer.Store(fixture.Company); writer.Store(fixture.Branch); writer.Store(fixture.Partner, fixture.Admin, fixture.Cleaner);
            await writer.SaveChangesAsync(); return fixture;
        }
        public static ITenantContext Context(string tenant) { var mock = new Mock<ITenantContext>(); mock.SetupGet(x => x.TenantId).Returns(tenant); return mock.Object; }
        public TenantSessionFactory Session() => new(Store, Context(Tenant));
        public PartnerService Service(ITenantSession session, CleaningSuite.Application.Notifications.IPushOutbox? push = null)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING") }).Build();
            return new(new PartnerStore(session), new ShiftWorkReservation(config, session), new ShiftRepository(session),
                new TenantProfileRepository(Store), Context(session.TenantId), push);
        }
        public async Task ConfigureCleaner(bool autoAccept)
        {
            using var writer = Store.DirtyTrackedSession(Tenant);
            var branch = await writer.LoadAsync<Branch>(Branch.Id); branch!.PreferredCleanerEmployeeId = Cleaner.Id;
            var cleaner = await writer.LoadAsync<Employee>(Cleaner.Id); cleaner!.KeycloakUserId = "cleaner-linked-login";
            cleaner.AutoAcceptWork = autoAccept;
            await writer.SaveChangesAsync();
        }
        public async Task<PartnerRequestDto> SubmitOffer()
        {
            using var session = Session(); var service = Service(session);
            var request = await service.Create(Partner.Id, new(Guid.NewGuid(), Branch.Id, Day, Day, "08:00"), default);
            return await service.Offer(Admin.Id, request.Id, new(request.Version, Cleaner.Id), default);
        }
        public void Dispose() => provider.Dispose();
    }
}
