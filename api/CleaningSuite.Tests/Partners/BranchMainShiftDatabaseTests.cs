using CleaningSuite.Application.Common;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Partners;
using CleaningSuite.Infrastructure.Persistence;
using FluentValidation;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Partners;

/// <summary>
/// A branch has one main shift. Each partner booking is one numbered visit on it, with its own cleaner, instructions
/// and frozen price. Runs on real PostgreSQL because identity, uniqueness and invoice blocking are persistence rules.
/// </summary>
[Collection("Database integration")]
public class BranchMainShiftDatabaseTests
{
    private static readonly TimeZoneInfo Zone = TenantTimeZone.Resolve("Europe/Helsinki");

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Two_bookings_for_one_branch_are_two_numbered_visits_on_one_main_shift()
    {
        using var f = await Fixture.Create();
        var first = await f.Accepted("08:00", f.Cleaner);
        var second = await f.Accepted("12:00", f.Cleaner);

        Assert.Equal(first.LinkedShiftId, second.LinkedShiftId);
        using var reader = f.Reader();
        var shift = Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.True(shift.IsBranchMain); Assert.True(shift.IsActive); Assert.Equal(2, shift.OccurrenceCounter);
        Assert.Equal(ShiftScheduleType.OnCallFlexible, shift.Schedule.Type);
        var visits = (await reader.Query<ShiftOccurrenceException>().ToListAsync()).OrderBy(x => x.Sequence).ToArray();
        Assert.Equal(new int?[] { 1, 2 }, visits.Select(x => x.Sequence).ToArray());
        Assert.Equal(new Guid?[] { first.Id, second.Id }, visits.Select(x => x.PartnerRequestId).ToArray());
        Assert.All(visits, x => Assert.Equal(ShiftOccurrenceException.SourcePartnerPortal, x.Source));
        // Both visits reach the cleaner through the shared roster rules.
        using var session = f.Session();
        var roster = await new ShiftRepository(session).ResolveRosterAsync(shift, Fixture.DayStart, Fixture.DayStart.AddDays(1), Zone, default);
        Assert.Equal(2, roster.Count); Assert.All(roster, x => Assert.Equal(f.Cleaner.Id, Assert.Single(x.EmployeeIds)));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_second_cleaner_accepting_keeps_the_first_cleaners_visit()
    {
        using var f = await Fixture.Create();
        var first = await f.Accepted("08:00", f.Cleaner);
        // Same property, same clock time, another cleaner: on a shared shift this must not end the first cleaner's work.
        var second = await f.Accepted("08:00", f.Second);

        using var reader = f.Reader();
        var shift = Assert.Single(await reader.Query<Shift>().ToListAsync());
        var visits = await reader.Query<ShiftOccurrenceException>().ToListAsync();
        Assert.Equal(2, visits.Count);
        // Work records and invoice lines find a visit by its start, so the two visits never share one.
        Assert.Equal(2, visits.Select(x => x.StartUtc).Distinct().Count());
        Assert.Equal(first.PlannedStartUtc, second.PlannedStartUtc);
        using var session = f.Session();
        var roster = await new ShiftRepository(session).ResolveRosterAsync(shift, Fixture.DayStart, Fixture.DayStart.AddDays(1), Zone, default);
        Assert.Equal(new[] { f.Cleaner.Id, f.Second.Id }.Order(), roster.SelectMany(x => x.EmployeeIds).Order());
        Assert.Equal("Accepted", (await reader.LoadAsync<PartnerCleaningRequest>(first.Id))!.Status);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Cancelling_one_booking_removes_only_its_visit_and_its_number_is_not_reused()
    {
        using var f = await Fixture.Create();
        var first = await f.Accepted("08:00", f.Cleaner);
        var second = await f.Accepted("12:00", f.Cleaner);
        using (var session = f.Session())
            await f.Service(session).Cancel(f.Admin.Id, first.Id, first.Version, true, "Guest left early", default);

        using (var reader = f.Reader())
        {
            var shift = Assert.Single(await reader.Query<Shift>().ToListAsync());
            Assert.True(shift.IsActive);
            Assert.Equal(second.Id, Assert.Single(await reader.Query<ShiftOccurrenceException>().ToListAsync()).PartnerRequestId);
            var cancelled = await reader.LoadAsync<PartnerCleaningRequest>(first.Id);
            Assert.Equal("Cancelled", cancelled!.Status); Assert.Null(cancelled.LinkedShiftId); Assert.Null(cancelled.OccurrenceStartUtc);
            Assert.Equal("Accepted", (await reader.LoadAsync<PartnerCleaningRequest>(second.Id))!.Status);
        }
        var third = await f.Accepted("16:00", f.Cleaner);
        using var after = f.Reader();
        Assert.Equal(3, (await after.LoadAsync<PartnerCleaningRequest>(third.Id))!.OccurrenceSequence);
        Assert.Equal(new int?[] { 2, 3 }, (await after.Query<ShiftOccurrenceException>().ToListAsync()).Select(x => x.Sequence).Order().ToArray());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Started_work_on_one_visit_does_not_lock_the_branchs_other_visits()
    {
        using var f = await Fixture.Create();
        var first = await f.Accepted("08:00", f.Cleaner);
        var second = await f.Accepted("12:00", f.Cleaner);
        await f.Record(first, f.Cleaner, submitted: false);

        using (var session = f.Session())
        {
            var service = f.Service(session);
            // The started visit is locked; the other one on the same shift can still be cancelled.
            await Assert.ThrowsAsync<ShiftConflictException>(() => service.Cancel(f.Admin.Id, first.Id, first.Version, true, "Too late", default));
        }
        using (var session = f.Session())
        {
            var service = f.Service(session);
            Assert.Equal("Cancelled", (await service.Cancel(f.Admin.Id, second.Id, second.Version, true, "Not needed", default)).Status);
            Assert.Equal("InProgress", (await service.Get(f.Partner.Id, first.Id, default)).Status);
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Rescheduling_a_booking_moves_its_own_visit_and_keeps_its_number()
    {
        using var f = await Fixture.Create();
        var first = await f.Accepted("08:00", f.Cleaner);
        var second = await f.Accepted("12:00", f.Cleaner);
        PartnerRequestDto moved;
        using (var session = f.Session())
        {
            var service = f.Service(session);
            var edited = await service.StaffEdit(f.Admin.Id, first.Id, new(first.Version, Fixture.Day, Fixture.Day, "15:00", "Come after three"), default);
            Assert.Null(edited.LinkedShiftId);
            var offered = await service.Offer(f.Admin.Id, first.Id, new(edited.Version, f.Second.Id), default);
            moved = await service.Accept(f.Second.Id, offered.OfferId!.Value, new(offered.Version), default);
        }
        using var reader = f.Reader();
        Assert.Single(await reader.Query<Shift>().ToListAsync());
        var visits = (await reader.Query<ShiftOccurrenceException>().ToListAsync()).OrderBy(x => x.Sequence).ToArray();
        Assert.Equal(2, visits.Length);
        Assert.Equal(1, visits[0].Sequence); Assert.Equal(f.Second.Id, visits[0].EmployeeId);
        Assert.Equal(moved.PlannedStartUtc, visits[0].StartUtc); Assert.Equal("Come after three", visits[0].Notes);
        Assert.Equal(2, visits[1].Sequence); Assert.Equal(f.Cleaner.Id, visits[1].EmployeeId); Assert.Equal(second.PlannedStartUtc, visits[1].StartUtc);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_booking_that_already_owns_a_shift_keeps_it()
    {
        using var f = await Fixture.Create();
        // A request accepted before main shifts existed: it owns a whole shift and has been sent back for a new cleaner.
        PartnerRequestDto request;
        using (var session = f.Session())
            request = await f.Service(session).Create(f.Partner.Id, new(Guid.NewGuid(), f.Branch.Id, Fixture.Day, Fixture.Day, "08:00"), default);
        var own = Shift.Create(f.Company.Id, f.Branch.Id, "Earlier partner visit", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null,
            Fixture.Day.ToDateTime(TimeOnly.MinValue), Fixture.Day.ToDateTime(TimeOnly.MinValue));
        own.PartnerRequestId = request.Id; own.Deactivate();
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            var stored = await writer.LoadAsync<PartnerCleaningRequest>(request.Id);
            stored!.LinkedShiftId = own.Id; writer.Store(stored); writer.Store(own); await writer.SaveChangesAsync();
        }
        PartnerRequestDto accepted;
        using (var session = f.Session())
        {
            var service = f.Service(session);
            var offered = await service.Offer(f.Admin.Id, request.Id, new(request.Version, f.Cleaner.Id), default);
            accepted = await service.Accept(f.Cleaner.Id, offered.OfferId!.Value, new(offered.Version), default);
        }
        Assert.Equal(own.Id, accepted.LinkedShiftId);
        using var reader = f.Reader();
        var shift = Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.False(shift.IsBranchMain); Assert.True(shift.IsActive); Assert.Equal(request.Id, shift.PartnerRequestId);
        Assert.Null((await reader.LoadAsync<PartnerCleaningRequest>(request.Id))!.OccurrenceStartUtc);
        Assert.Single(await reader.Query<ShiftAssignment>().Where(x => x.IsActive).ToListAsync());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task The_cleaner_sees_each_visits_own_instructions_and_photo_sections()
    {
        using var f = await Fixture.Create();
        using (var session = f.Session())
            await f.Service(session).UpdateSettings(f.Partner.Id, new PartnerSettingsInput(["Before", "Keys"], null, "Use the side door."), default);
        var first = await f.Accepted("08:00", f.Cleaner, "Change the linen");
        using (var session = f.Session())
            await f.Service(session).UpdateSettings(f.Partner.Id, new PartnerSettingsInput(["After"], null, "Use the side door."), default);
        var second = await f.Accepted("12:00", f.Cleaner, "Water the plants");

        using var read = f.Session();
        var config = Fixture.Config();
        var handler = new QualityCycleHandlers.GetMyShiftsQueryHandler(new ShiftRepository(read), new QualityCycleRepository(read, f.Store),
            new EmployeeRepository(read), new ShiftWorkReservation(config, read), f.Profiles.Object, Fixture.Context(f.Tenant));
        var mine = await handler.Handle(new(f.Cleaner.Id, Fixture.DayStart, Fixture.DayStart.AddDays(1)), default);

        Assert.Equal(2, mine.Count);
        Assert.Equal("Use the side door.\n\nChange the linen", mine[0].Notes); Assert.Equal(new[] { "Before", "Keys" }, mine[0].RequiredPhotoLabels);
        Assert.Equal("Use the side door.\n\nWater the plants", mine[1].Notes); Assert.Equal(new[] { "After" }, mine[1].RequiredPhotoLabels);
        Assert.All(mine, x => Assert.Equal(first.LinkedShiftId, x.ShiftId));
        Assert.Equal(mine[0].ShiftName, mine[1].ShiftName);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Each_visit_is_invoiced_once_at_the_price_frozen_on_its_own_booking()
    {
        using var f = await Fixture.Create();
        var first = await f.Accepted("08:00", f.Cleaner);
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            // The property price rises after the first booking; that booking keeps its 100 EUR at 25.5 % VAT.
            var branch = await writer.LoadAsync<Branch>(f.Branch.Id);
            branch!.SetPricing(300, 14, 60, effectiveFrom: new DateOnly(2026, 11, 2)); writer.Store(branch); await writer.SaveChangesAsync();
        }
        var second = await f.Accepted("12:00", f.Cleaner);
        await f.Record(first, f.Cleaner, submitted: true);
        var month = new ShiftInvoiceRequest(f.Company.Id, [], new(2026, 11, 1), new(2026, 11, 30),
            new Address { Street = "Customer 1", PostalCode = "00100", City = "Helsinki" }, PartnerMonth: true);

        Invoice issued;
        using (var session = f.Session())
        {
            var invoices = f.Invoices(session);
            var preview = await invoices.PreviewAsync(month, default);
            var line = Assert.Single(preview.Invoice.Lines);
            Assert.Equal(100m, line.NetAmount); Assert.Equal(25.5m, line.VatRatePercent);
            Assert.Equal(first.LinkedShiftId, line.ShiftId);
            Assert.Equal(new[] { first.PlannedStartUtc!.Value }, line.OccurrenceStartsUtc);
            issued = await invoices.IssueAsync(month with { PreviewToken = preview.PreviewToken }, default);
        }
        Assert.Equal(100m, issued.Total.Net);

        // The second visit is completed later in the same month. The first invoice must not hide it, nor be billed again.
        await f.Record(second, f.Cleaner, submitted: true);
        using (var session = f.Session())
        {
            var invoices = f.Invoices(session);
            var preview = await invoices.PreviewAsync(month, default);
            var line = Assert.Single(preview.Invoice.Lines);
            Assert.Equal(300m, line.NetAmount); Assert.Equal(14m, line.VatRatePercent);
            Assert.Equal(new[] { second.PlannedStartUtc!.Value }, line.OccurrenceStartsUtc);
            await invoices.IssueAsync(month with { PreviewToken = preview.PreviewToken }, default);
        }
        using (var session = f.Session())
            await Assert.ThrowsAsync<ValidationException>(() => f.Invoices(session).PreviewAsync(month, default));
        using (var session = f.Session())
        {
            // An invoiced visit is locked; this is checked per visit, not for the whole shared shift.
            var error = await Assert.ThrowsAsync<ShiftConflictException>(() => f.Service(session).Cancel(f.Admin.Id, first.Id, first.Version, true, "Late", default));
            Assert.Contains("Work has started", error.Message);
        }
        using var reader = f.Reader();
        Assert.Equal(new[] { 100m, 300m }, (await reader.Query<Invoice>().ToListAsync()).Select(x => x.Total.Net).Order().ToArray());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_cancelled_booking_is_never_invoiced_and_does_not_block_the_month()
    {
        using var f = await Fixture.Create();
        var kept = await f.Accepted("08:00", f.Cleaner);
        var dropped = await f.Accepted("12:00", f.Cleaner);
        using (var session = f.Session()) await f.Service(session).Cancel(f.Admin.Id, dropped.Id, dropped.Version, true, "Not needed", default);
        await f.Record(kept, f.Cleaner, submitted: true);
        var month = new ShiftInvoiceRequest(f.Company.Id, [], new(2026, 11, 1), new(2026, 11, 30), PartnerMonth: true);
        using var read = f.Session();
        var preview = await f.Invoices(read).PreviewAsync(month, default);
        Assert.Equal(new[] { kept.PlannedStartUtc!.Value }, Assert.Single(preview.Invoice.Lines).OccurrenceStartsUtc);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task The_main_shift_price_cannot_be_edited_apart_from_the_property()
    {
        using var f = await Fixture.Create();
        var accepted = await f.Accepted("08:00", f.Cleaner);
        using var session = f.Session();
        var settings = new ShiftBillingSettingsService(new ShiftRepository(session), new ShiftBillingRepository(session), new InvoiceRepository(session),
            f.Profiles.Object, Fixture.Context(f.Tenant), new ShiftWorkReservation(Fixture.Config(), session), new BranchRepository(session));
        var shiftId = accepted.LinkedShiftId!.Value;
        await Assert.ThrowsAsync<ShiftConflictException>(() => settings.SaveAsync(shiftId, new("PerOccurrence", 50, 25.5m, new(2026, 11, 1)), default));
        await Assert.ThrowsAsync<ShiftConflictException>(() => settings.SelectBranchPriceAsync(shiftId, f.Branch.Versions[0].EffectiveFrom, default));
        // A partner booking's price is frozen on its request, so its visit takes no override either.
        await Assert.ThrowsAsync<ShiftConflictException>(() => settings.SaveOccurrenceAsync(shiftId,
            new(DateTime.SpecifyKind(accepted.PlannedStartUtc!.Value, DateTimeKind.Utc), 10, "Discount"), default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_booked_visit_and_its_main_shift_cannot_be_removed_from_the_shift_screens()
    {
        using var f = await Fixture.Create();
        var accepted = await f.Accepted("08:00", f.Cleaner);
        var shiftId = accepted.LinkedShiftId!.Value;
        using var session = f.Session();
        var repository = new ShiftRepository(session);
        var visit = Assert.Single(await repository.ListOccurrenceExceptionsAsync(shiftId, Fixture.DayStart, Fixture.DayStart.AddDays(1), default));
        // The booking's visit is changed only through its partner request.
        await Assert.ThrowsAsync<ShiftConflictException>(() => new ShiftHandlers.DeleteShiftOccurrenceExceptionCommandHandler(repository, f.Profiles.Object, Fixture.Context(f.Tenant))
            .Handle(new(shiftId, visit.Id), default));
        var shift = (await repository.GetAsync(shiftId, default))!;
        // Switching the shared shift off would silently take the visit away from its cleaner.
        await Assert.ThrowsAsync<ShiftConflictException>(() => PartnerShiftGuard.EnsureNoBookingsAsync(repository, shift, default));
        Assert.Throws<ShiftConflictException>(() => PartnerShiftGuard.EnsureMainStaysPut(shift, shift.CompanyId, shift.BranchId, new ShiftSchedule { Type = ShiftScheduleType.Weekly }));
        PartnerShiftGuard.EnsureMainStaysPut(shift, shift.CompanyId, shift.BranchId, shift.Schedule);
        using (var cancel = f.Session()) await f.Service(cancel).Cancel(f.Admin.Id, accepted.Id, accepted.Version, true, "Not needed", default);
        using var after = f.Session();
        await PartnerShiftGuard.EnsureNoBookingsAsync(new ShiftRepository(after), shift, default);
    }

    private sealed class Fixture : IDisposable
    {
        public static DateOnly Day => new(2026, 11, 10);
        public static DateTime DayStart => TenantTimeZone.StartOfLocalDay(Day.ToDateTime(TimeOnly.MinValue), Zone);
        private readonly ServiceProvider provider;
        public IDocumentStore Store { get; }
        public string Tenant { get; } = "branch-main-" + Guid.NewGuid().ToString("N");
        public Company Company { get; } = Company.Create(Guid.NewGuid().ToString(), "Owner company");
        public Branch Branch { get; private set; } = null!;
        public Employee Partner { get; private set; } = null!;
        public Employee Admin { get; } = new() { Role = Employee.RoleAdmin, KeycloakUserId = "synthetic-admin", IsActive = true };
        public Employee Cleaner { get; } = new() { Role = Employee.RoleEmployee, KeycloakUserId = "synthetic-cleaner", FirstName = "First", LastName = "Cleaner", IsActive = true, AutoAcceptWork = false };
        public Employee Second { get; } = new() { Role = Employee.RoleEmployee, KeycloakUserId = "synthetic-second", FirstName = "Second", LastName = "Cleaner", IsActive = true, AutoAcceptWork = false };
        public Mock<ITenantProfileRepository> Profiles { get; } = new();
        private Fixture(ServiceProvider provider) { this.provider = provider; Store = provider.GetRequiredService<IDocumentStore>(); }

        public static async Task<Fixture> Create()
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "branch_main_" + Guid.NewGuid().ToString("N"));
            var fixture = new Fixture(services.BuildServiceProvider());
            await fixture.Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            fixture.Company.IsPartnerEnabled = true;
            fixture.Branch = Branch.Create(fixture.Company.Id, "Apartment");
            fixture.Branch.SetPricing(100, 25.5m, 120, effectiveFrom: new DateOnly(2026, 1, 1)); fixture.Branch.SetPropertyDetails(34, "Deep");
            fixture.Partner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = fixture.Company.Id, IsActive = true };
            fixture.Profiles.Setup(p => p.GetAsync(fixture.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki",
                CompanyName = "Sample supplier", BusinessId = "1234567-8", CompanyAddress = new() { Street = "Sample 1", City = "Helsinki", PostalCode = "00100" },
                BankAccountIBAN = "FI2112345600000785", BankBic = "NDEAFIHH", InvoiceIssuerIsSample = true });
            using var writer = fixture.Store.LightweightSession(fixture.Tenant);
            writer.Store(fixture.Company); writer.Store(fixture.Branch); writer.Store(fixture.Partner, fixture.Admin, fixture.Cleaner, fixture.Second);
            await writer.SaveChangesAsync(); return fixture;
        }

        public static ITenantContext Context(string tenant) { var mock = new Mock<ITenantContext>(); mock.SetupGet(x => x.TenantId).Returns(tenant); return mock.Object; }
        public static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING") }).Build();
        public TenantSessionFactory Session() => new(Store, Context(Tenant));
        public IQuerySession Reader() => Store.QuerySession(Tenant);
        public PartnerService Service(ITenantSession session) => new(new PartnerStore(session), new ShiftWorkReservation(Config(), session),
            new ShiftRepository(session), Profiles.Object, Context(session.TenantId));
        public ShiftInvoiceService Invoices(TenantSessionFactory session) => new(new ShiftRepository(session), new ShiftBillingRepository(session),
            new InvoiceRepository(session), new CompanyRepository(session), new BranchRepository(session), new QualityCycleRepository(session, Store),
            Profiles.Object, Context(Tenant), new ShiftWorkReservation(Config(), session), TimeProvider.System, new PartnerInvoiceSource(new PartnerStore(session)));

        /// <summary>A booking at a clock time on the test day, offered to and accepted by the given cleaner.</summary>
        public async Task<PartnerRequestDto> Accepted(string time, Employee cleaner, string? notes = null)
        {
            using var session = Session(); var service = Service(session);
            var request = await service.Create(Partner.Id, new(Guid.NewGuid(), Branch.Id, Day, Day, time, notes), default);
            var offered = await service.Offer(Admin.Id, request.Id, new(request.Version, cleaner.Id), default);
            return await service.Accept(cleaner.Id, offered.OfferId!.Value, new(offered.Version), default);
        }

        /// <summary>The cleaner's work record for a booking's visit: started, and optionally finished.</summary>
        public async Task Record(PartnerRequestDto booking, Employee cleaner, bool submitted)
        {
            using var writer = Store.LightweightSession(Tenant);
            var stored = await writer.LoadAsync<PartnerCleaningRequest>(booking.Id);
            var start = stored!.OccurrenceStartUtc!.Value;
            writer.Store(new QualityCycleForm { Id = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"), ShiftId = stored.LinkedShiftId!.Value, CompanyId = Company.Id, BranchId = Branch.Id, EmployeeId = cleaner.Id,
                ShiftOccurrenceUtc = start, ShiftOccurrenceEndUtc = start.AddMinutes(120), StartedAtUtc = start,
                IsSubmitted = submitted, EndedAtUtc = submitted ? start.AddMinutes(110) : null, SubmittedUtc = submitted ? start.AddMinutes(110) : null });
            await writer.SaveChangesAsync();
        }

        public void Dispose() => provider.Dispose();
    }
}
