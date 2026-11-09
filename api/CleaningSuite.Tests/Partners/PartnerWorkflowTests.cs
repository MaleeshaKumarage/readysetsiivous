using CleaningSuite.Application.Common;
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

[Collection("Database integration")]
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
    public async Task Partners_see_price_and_duration_but_never_the_cleaner_name()
    {
        using var fixture = await Fixture.Create();
        var request = await fixture.SubmitOffer();
        using (var session = fixture.Session())
            await fixture.Service(session).Accept(fixture.Cleaner.Id, request.OfferId!.Value, new(request.Version), default);
        using var session2 = fixture.Session();
        var service = fixture.Service(session2);
        var partnerView = await service.Get(fixture.Partner.Id, request.Id, default);
        Assert.Equal(100m, partnerView.PriceNet);
        Assert.Equal(25.5m, partnerView.VatRatePercent);
        Assert.Equal(120, partnerView.EstimatedDurationMinutes);
        Assert.Null(partnerView.AcceptedCleanerName);
        var staffView = Assert.Single(await service.Requests(fixture.Admin.Id, true, null, default), x => x.Id == request.Id);
        Assert.False(string.IsNullOrWhiteSpace(staffView.AcceptedCleanerName));
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
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).Cancel(fixture.Partner.Id, offer.Id, reoffer.Version, false, "Owner travel", default));
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Partner_cancellation_requires_a_reason_and_records_it()
    {
        using var fixture = await Fixture.Create(); var offer = await fixture.SubmitOffer();
        using (var session = fixture.Session())
        {
            foreach (var blank in new string?[] { null, "", "   ", new string('x', 1001) })
                await Assert.ThrowsAsync<ValidationException>(() => fixture.Service(session).Cancel(fixture.Partner.Id, offer.Id, offer.Version, false, blank, default));
        }
        using (var reader = fixture.Store.QuerySession(fixture.Tenant))
            Assert.NotEqual("Cancelled", (await reader.LoadAsync<PartnerCleaningRequest>(offer.Id))!.Status);
        using (var session = fixture.Session())
        {
            var padded = "  " + new string('x', 1000) + "  "; // 1000 characters once trimmed: accepted.
            var cancelled = await fixture.Service(session).Cancel(fixture.Partner.Id, offer.Id, offer.Version, false, padded, default);
            Assert.Equal("Cancelled", cancelled.Status);
        }
        using var check = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Equal(new string('x', 1000), (await check.LoadAsync<PartnerCleaningRequest>(offer.Id))!.CancellationReason);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Declining_an_offer_that_is_no_longer_waiting_says_so_instead_of_reporting_a_change()
    {
        using var fixture = await Fixture.Create();
        var offer = await fixture.SubmitOffer();
        using (var session = fixture.Session()) await fixture.Service(session).Accept(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version), default);
        using var declining = fixture.Session();
        // The cleaner still holds the offer at its old version; the real reason is that it is no longer waiting for an answer.
        var ex = await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(declining).Decline(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version), default));
        Assert.Contains("no longer waiting for your answer", ex.Message);
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
    public async Task Accepted_partner_visit_carries_the_company_quality_cycle_template()
    {
        using var fixture = await Fixture.Create(); PartnerRequestDto offer;
        var template = QualityCycleTemplate.Create("Monthly checklist", ["Corridor", "Gym"], companyId: fixture.Company.Id);
        fixture.Company.IsQualityCycleEnabled = true; fixture.Company.QualityCycleTemplateId = template.Id;
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { writer.Store(template); writer.Store(fixture.Company); await writer.SaveChangesAsync(); }
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var request = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day.AddDays(1)), default);
            offer = await service.Offer(fixture.Admin.Id, request.Id, new(request.Version, fixture.Cleaner.Id), default);
        }
        PartnerRequestDto accepted;
        using (var session = fixture.Session())
            accepted = await fixture.Service(session).Accept(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version, null), default);
        Assert.Equal("Accepted", accepted.Status);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var shift = await reader.LoadAsync<Shift>(accepted.LinkedShiftId!.Value);
        Assert.Equal(template.Id, shift!.QualityCycleTemplateId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Automatic_offer_without_a_time_is_a_flexible_visit_across_the_requested_window()
    {
        using var fixture = await Fixture.Create();
        using (var session = fixture.Session()) await fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, true, default);
        PartnerRequestDto accepted;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var request = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day.AddDays(1)), default);
            accepted = await service.Offer(fixture.Admin.Id, request.Id, new(request.Version, fixture.Cleaner.Id), default);
        }
        Assert.Equal("Accepted", accepted.Status);
        Assert.Null(accepted.PlannedStartUtc);
        Assert.Null(accepted.PlannedEndUtc);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var shift = await reader.LoadAsync<Shift>(accepted.LinkedShiftId!.Value);
        Assert.Equal(Fixture.Day.ToDateTime(TimeOnly.MinValue), shift!.ValidFrom);
        Assert.Equal(Fixture.Day.AddDays(1).ToDateTime(TimeOnly.MinValue), shift.ValidUntil);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Automatic_preference_without_sign_in_gets_an_ordinary_offer_not_automatic_acceptance()
    {
        using var fixture = await Fixture.Create();
        // Added but not yet signed in: the stored preference is on, but automatic acceptance needs a login to accept.
        var pending = new Employee { Role = Employee.RoleEmployee, KeycloakUserId = "", FirstName = "Pending", LastName = "Cleaner", AutoAcceptWork = true };
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { writer.Store(pending); await writer.SaveChangesAsync(); }
        PartnerRequestDto offered;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var request = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
            offered = await service.Offer(fixture.Admin.Id, request.Id, new(request.Version, pending.Id), default);
        }
        Assert.Equal("Offered", offered.Status);
        Assert.Equal(pending.Id, offered.OfferedEmployeeId);
        Assert.Null(offered.LinkedShiftId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Manual_offer_accepted_without_a_time_becomes_a_flexible_visit()
    {
        using var fixture = await Fixture.Create(); PartnerRequestDto offer;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var request = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day.AddDays(1)), default);
            offer = await service.Offer(fixture.Admin.Id, request.Id, new(request.Version, fixture.Cleaner.Id), default);
        }
        PartnerRequestDto accepted;
        using (var session = fixture.Session())
            accepted = await fixture.Service(session).Accept(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version, null), default);
        Assert.Equal("Accepted", accepted.Status);
        Assert.Null(accepted.PlannedStartUtc);
    }

    [Fact]
    public void New_employees_default_to_automatic_work_acceptance() => Assert.True(new Employee().AutoAcceptWork);

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Accept_versus_cancel_cannot_leave_a_cancelled_request_with_a_live_shift()
    {
        using var fixture = await Fixture.Create(); var offer = await fixture.SubmitOffer();
        using var a = fixture.Session(); using var b = fixture.Session();
        async Task<bool> Attempt(Func<Task> action) { try { await action(); return true; }
            catch (Exception ex) when (ex is ShiftConflictException or CleaningSuite.Application.Common.NotFoundException) { return false; } }
        var outcomes = await Task.WhenAll(Attempt(async () => { await fixture.Service(a).Accept(fixture.Cleaner.Id, offer.OfferId!.Value, new(offer.Version), default); }),
            Attempt(async () => { await fixture.Service(b).Cancel(fixture.Partner.Id, offer.Id, offer.Version, false, "Owner travel", default); }));
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
        // Staff are alerted by app push, so only the company receives a RequestAccepted email row.
        Assert.Equal("Company", Assert.Single(notices.Where(x => x.Kind == "RequestAccepted")).Audience);
        Assert.DoesNotContain(notices, x => x.Audience == "Staff");
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
    public async Task Automatic_offer_overlap_failure_commits_nothing()
    {
        using var fixture = await Fixture.Create();
        using (var session = fixture.Session()) await fixture.Service(session).SetWorkPreferences(fixture.Cleaner.Id, true, default);
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
    public async Task Owner_edits_property_details_but_never_price_company_or_active_flag()
    {
        using var fixture = await Fixture.Create();
        using var session = fixture.Session();
        var service = fixture.Service(session);
        var before = fixture.Branch;
        var updated = await service.UpdateBranch(fixture.Partner.Id, before.Id,
            new("Renamed flat", "New street 2", "00100", "Helsinki", "FI", "+358 40 123", "owner@example.test", 3, 4), default);
        Assert.Equal("Renamed flat", updated.Name);
        Assert.Equal("New street 2", updated.Street);
        Assert.Equal("owner@example.test", updated.ContactEmail);
        Assert.Equal(3, updated.RoomCount);
        Assert.Equal(4, updated.BedCount);
        Assert.Equal(before.CurrentPriceNet, updated.PriceNet);
        Assert.Equal(before.CurrentVatRatePercent, updated.VatRatePercent);
        Assert.Equal(before.CurrentEstimatedDurationMinutes, updated.EstimatedDurationMinutes);
        Assert.Equal(before.PricingVersion, updated.PricingVersion);
        Assert.True(updated.IsActive);
        Assert.Equal(fixture.Company.Id, updated.CompanyId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Owner_property_edit_rejects_invalid_details_and_other_companies_properties()
    {
        using var fixture = await Fixture.Create();
        var other = await StoreBranch(fixture, Guid.NewGuid(), "Other company flat", priced: true);
        using var session = fixture.Session();
        var service = fixture.Service(session);
        var id = fixture.Branch.Id;
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateBranch(fixture.Partner.Id, id, new(""), default));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateBranch(fixture.Partner.Id, id, new("Flat", ContactEmail: "not-an-email"), default));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateBranch(fixture.Partner.Id, id, new("Flat", RoomCount: 10001), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateBranch(fixture.Partner.Id, other.Id, new("Flat"), default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Owner_withdraws_a_pending_quote_and_the_staff_queue_drops_it()
    {
        using var fixture = await Fixture.Create();
        var pending = await StoreBranch(fixture, fixture.Company.Id, "Cabin", priced: false);
        using (var session = fixture.Session()) await fixture.Service(session).Quote(fixture.Partner.Id, pending.Id, default);
        using (var session = fixture.Session())
        {
            Assert.Contains(await fixture.Service(session).Quotes(fixture.Admin.Id, default), x => x.Id == pending.Id);
            var withdrawn = await fixture.Service(session).WithdrawQuote(fixture.Partner.Id, pending.Id, default);
            Assert.Null(withdrawn.QuoteRequestedUtc);
        }
        using (var session = fixture.Session())
        {
            Assert.DoesNotContain(await fixture.Service(session).Quotes(fixture.Admin.Id, default), x => x.Id == pending.Id);
            // Withdrawing again, or withdrawing a priced property, has nothing pending to withdraw.
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).WithdrawQuote(fixture.Partner.Id, pending.Id, default));
            await Assert.ThrowsAsync<ShiftConflictException>(() => fixture.Service(session).WithdrawQuote(fixture.Partner.Id, fixture.Branch.Id, default));
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Staff_reject_requires_a_reason_and_a_pending_quote_and_the_owner_sees_the_reason()
    {
        using var fixture = await Fixture.Create();
        var outside = await StoreBranch(fixture, fixture.Company.Id, "Outside area", priced: false);
        using (var session = fixture.Session()) await fixture.Service(session).Quote(fixture.Partner.Id, outside.Id, default);
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            await Assert.ThrowsAsync<ValidationException>(() => service.RejectQuote(fixture.Admin.Id, outside.Id, new(" "), default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RejectQuote(fixture.Partner.Id, outside.Id, new("Not allowed"), default));
            var rejected = await service.RejectQuote(fixture.Admin.Id, outside.Id, new("  Outside the service area  "), default);
            Assert.Equal("Outside the service area", rejected.QuoteRejectionReason);
            Assert.NotNull(rejected.QuoteRejectedUtc);
            Assert.Null(rejected.QuoteRequestedUtc);
            await Assert.ThrowsAsync<ShiftConflictException>(() => service.RejectQuote(fixture.Admin.Id, outside.Id, new("Again"), default));
        }
        using (var session = fixture.Session())
        {
            Assert.DoesNotContain(await fixture.Service(session).Quotes(fixture.Admin.Id, default), x => x.Id == outside.Id);
            var owner = Assert.Single(await fixture.Service(session).Branches(fixture.Partner.Id, false, null, default), x => x.Id == outside.Id);
            Assert.Equal("Outside the service area", owner.QuoteRejectionReason);
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Rejected_owner_can_request_again_and_pricing_clears_the_rejection()
    {
        using var fixture = await Fixture.Create();
        var branch = await StoreBranch(fixture, fixture.Company.Id, "Second try", priced: false);
        using (var session = fixture.Session()) await fixture.Service(session).Quote(fixture.Partner.Id, branch.Id, default);
        using (var session = fixture.Session()) await fixture.Service(session).RejectQuote(fixture.Admin.Id, branch.Id, new("Too remote"), default);
        using (var session = fixture.Session())
        {
            var requested = await fixture.Service(session).Quote(fixture.Partner.Id, branch.Id, default);
            Assert.NotNull(requested.QuoteRequestedUtc);
            Assert.Null(requested.QuoteRejectionReason);
        }
        using (var session = fixture.Session())
        {
            var priced = await fixture.Service(session).Price(fixture.Admin.Id, branch.Id, new(90, 25.5m, 120), default);
            Assert.True(priced.PricingReady);
            Assert.Null(priced.QuoteRejectedUtc);
            Assert.Null(priced.QuoteRejectionReason);
        }
    }

    private static async Task<Branch> StoreBranch(Fixture fixture, Guid companyId, string name, bool priced)
    {
        var branch = Branch.Create(companyId, name);
        if (priced) branch.SetPricing(100, 25.5m, 120);
        using var writer = fixture.Store.LightweightSession(fixture.Tenant);
        writer.Store(branch); await writer.SaveChangesAsync();
        return branch;
    }

    private sealed class Fixture : IDisposable
    {
        public static DateOnly Day => new(2026, 11, 10);
        private readonly ServiceProvider provider;
        public IDocumentStore Store { get; }
        public string Tenant { get; } = "partner-test-" + Guid.NewGuid().ToString("N");
        public Company Company { get; } = Company.Create(Guid.NewGuid().ToString(), "Owner company");
        public Branch Branch { get; private set; } = null!;
        public Employee Partner { get; private set; } = null!;
        public Employee Admin { get; } = new() { Role = Employee.RoleAdmin, KeycloakUserId = "synthetic-admin", AutoAcceptWork = false };
        public Employee Cleaner { get; } = new() { Role = Employee.RoleEmployee, KeycloakUserId = "synthetic-cleaner", FirstName = "Cleaner", LastName = "Test", AutoAcceptWork = false };
        private Fixture(ServiceProvider provider) { this.provider = provider; Store = provider.GetRequiredService<IDocumentStore>(); }
        public static async Task<Fixture> Create()
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "partner_" + Guid.NewGuid().ToString("N"));
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
        public async Task<PartnerRequestDto> SubmitOffer()
        {
            using var session = Session(); var service = Service(session);
            var request = await service.Create(Partner.Id, new(Guid.NewGuid(), Branch.Id, Day, Day, "08:00"), default);
            return await service.Offer(Admin.Id, request.Id, new(request.Version, Cleaner.Id), default);
        }
        public void Dispose() => provider.Dispose();
    }
}
