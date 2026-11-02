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
    public async Task Preferred_cleaner_receives_direct_offer_without_staff_broadcast_and_decline_falls_back()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(false);
        var clientId = Guid.NewGuid(); PartnerRequestDto created;
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            created = await service.Create(fixture.Partner.Id, new(clientId, fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
            var retry = await service.Create(fixture.Partner.Id, new(clientId, fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
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
            var exact = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
            Assert.Equal("Accepted", exact.Status); Assert.NotNull(exact.LinkedShiftId);
        }
        using (var session = fixture.Session())
        {
            var conflict = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "09:00"), default);
            Assert.Equal("Offered", conflict.Status); Assert.Null(conflict.LinkedShiftId);
        }
        using (var session = fixture.Session())
        {
            var flexible = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day.AddDays(1), Fixture.Day.AddDays(2)), default);
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
            new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
        using (var session = fixture.Session())
        {
            var service = fixture.Service(session);
            var cancelled = await service.Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day.AddDays(1), Fixture.Day.AddDays(1)), default);
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
        var body = new CreatePartnerRequest(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00");
        using var first = fixture.Session(); using var second = fixture.Session();
        var results = await Task.WhenAll(fixture.Service(first).Create(fixture.Partner.Id, body, default), fixture.Service(second).Create(fixture.Partner.Id, body, default));
        Assert.All(results, x => Assert.Equal("Accepted", x.Status));
        Assert.Equal(results[0].LinkedShiftId, results[1].LinkedShiftId);
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Single(await reader.Query<PartnerCleaningRequest>().ToListAsync());
        Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.Single(await reader.Query<ShiftAssignment>().ToListAsync());
        Assert.Single(await reader.Query<PartnerNotification>().Where(x => x.Kind == "RequestAccepted" && x.Audience == "Company").ToListAsync());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Invalid_preferred_cleaner_falls_back_without_changing_saved_branch_or_historical_requests()
    {
        using var fixture = await Fixture.Create(); await fixture.ConfigureCleaner(false);
        using (var writer = fixture.Store.DirtyTrackedSession(fixture.Tenant))
        { var cleaner = await writer.LoadAsync<Employee>(fixture.Cleaner.Id); cleaner!.IdentitySyncPending = true; await writer.SaveChangesAsync(); }
        using (var session = fixture.Session())
        {
            var request = await fixture.Service(session).Create(fixture.Partner.Id, new(Guid.NewGuid(), fixture.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
            Assert.Equal("Requested", request.Status);
        }
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        Assert.Contains(await reader.Query<PartnerNotification>().ToListAsync(), x => x.Kind == "PreferredCleanerUnavailable" && x.Audience == "Staff");
        Assert.Equal(fixture.Cleaner.Id, (await reader.LoadAsync<Branch>(fixture.Branch.Id))!.PreferredCleanerEmployeeId);
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
        public Employee Admin { get; } = new() { Role = Employee.RoleAdmin };
        public Employee Cleaner { get; } = new() { Role = Employee.RoleEmployee, FirstName = "Cleaner", LastName = "Test" };
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
        public PartnerService Service(ITenantSession session)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING") }).Build();
            return new(new PartnerStore(session), new ShiftWorkReservation(config, session), new ShiftRepository(session),
                new TenantProfileRepository(Store), Context(session.TenantId));
        }
        public async Task ConfigureCleaner(bool autoAccept)
        {
            using var writer = Store.DirtyTrackedSession(Tenant);
            var branch = await writer.LoadAsync<Branch>(Branch.Id); branch!.PreferredCleanerEmployeeId = Cleaner.Id;
            var cleaner = await writer.LoadAsync<Employee>(Cleaner.Id); cleaner!.KeycloakUserId = "cleaner-linked-login";
            cleaner.AutoAcceptPartnerRequests = autoAccept;
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
