using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Employees;

[Collection("Database integration")]
public sealed class PartnerWorkforceTransitionTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Pending_offers_bookings_and_started_work_block_transition_but_completed_history_does_not()
    {
        using var fixture = await Fixture.Create();
        var offerEmployee = Guid.NewGuid(); var bookingEmployee = Guid.NewGuid(); var startedEmployee = Guid.NewGuid(); var historyEmployee = Guid.NewGuid();
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant))
        {
            writer.Store(new PartnerCleaningRequest { OfferedEmployeeId = offerEmployee, Status = "Offered" });
            writer.Store(new Booking { EmployeeId = bookingEmployee.ToString(), Status = Booking.StatusConfirmed, StartUtc = Now.AddDays(-1), EndUtc = Now.AddDays(-1).AddHours(2) });
            writer.Store(new QualityCycleForm { EmployeeId = startedEmployee, Token = Guid.NewGuid().ToString("N"), StartedAtUtc = Now.AddDays(-1) });
            writer.Store(new Booking { EmployeeId = historyEmployee.ToString(), Status = Booking.StatusCompleted, StartUtc = Now.AddDays(-10), EndUtc = Now.AddDays(-10).AddHours(2) });
            writer.Store(new QualityCycleForm { EmployeeId = historyEmployee, Token = Guid.NewGuid().ToString("N"), StartedAtUtc = Now.AddDays(-10), IsSubmitted = true });
            await writer.SaveChangesAsync();
        }
        foreach (var employee in new[] { offerEmployee, bookingEmployee, startedEmployee })
        {
            using var session = fixture.Session();
            await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => fixture.Guard(session).EnsureNoOutstandingWorkAsync(employee, default));
        }
        using var history = fixture.Session();
        await fixture.Guard(history).EnsureNoOutstandingWorkAsync(historyEmployee, default);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Far_future_recurring_and_individual_visits_block_but_replaced_or_completed_past_rosters_do_not()
    {
        using var fixture = await Fixture.Create();
        var recurring = Guid.NewGuid(); var extraEmployee = Guid.NewGuid(); var overrideEmployee = Guid.NewGuid(); var replaced = Guid.NewGuid(); var past = Guid.NewGuid();
        var far = Now.AddYears(1).Date.AddHours(8);
        var recurringShift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Recurring", new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, far.Date, null);
        var extraShift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Extra", new() { Type = ShiftScheduleType.OnCallFlexible }, null, Now.Date, null);
        extraShift.Deactivate(); // Retained work must not become a partner assignment when reactivated.
        var overrideShift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Override", new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, far.Date, far.Date);
        var pastShift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Past", new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, Now.AddDays(-10).Date, Now.AddDays(-1).Date);
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant))
        {
            writer.Store(recurringShift, extraShift, overrideShift, pastShift);
            var assignment = ShiftAssignment.Create(recurringShift.Id, recurring, "Test"); assignment.Periods = [new() { FromUtc = far }]; writer.Store(assignment);
            var original = ShiftAssignment.Create(overrideShift.Id, replaced, "Test"); original.Periods = [new() { FromUtc = far }]; writer.Store(original);
            var historical = ShiftAssignment.Create(pastShift.Id, past, "Test"); historical.Periods = [new() { FromUtc = Now.AddDays(-10).Date }]; writer.Store(historical);
            writer.Store(ShiftOccurrenceException.Create(extraShift.Id, null, far, far.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Test", extraEmployee));
            writer.Store(ShiftOccurrenceAssignment.Create(overrideShift.Id, far, null, OccurrenceAssignmentMode.Replace, [overrideEmployee], "Test"));
            for (var day = Now.AddDays(-10).Date; day < Now.Date; day = day.AddDays(1))
                writer.Store(new QualityCycleForm { EmployeeId = past, ShiftId = pastShift.Id, ShiftOccurrenceUtc = day.AddHours(8), IsSubmitted = true, Token = Guid.NewGuid().ToString("N") });
            await writer.SaveChangesAsync();
        }
        foreach (var employee in new[] { recurring, extraEmployee, overrideEmployee })
        {
            using var session = fixture.Session();
            await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => fixture.Guard(session).EnsureNoOutstandingWorkAsync(employee, default));
        }
        foreach (var employee in new[] { replaced, past })
        {
            using var session = fixture.Session();
            await fixture.Guard(session).EnsureNoOutstandingWorkAsync(employee, default);
        }
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant))
        {
            var form = await writer.Query<QualityCycleForm>().FirstAsync(f => f.EmployeeId == past);
            writer.Delete(form); await writer.SaveChangesAsync();
        }
        using var outstandingPast = fixture.Session();
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => fixture.Guard(outstandingPast).EnsureNoOutstandingWorkAsync(past, default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Employee_update_checks_work_before_mutation_and_retains_finished_history()
    {
        using var fixture = await Fixture.Create();
        var company = Company.Create("test", "Owner"); company.IsPartnerEnabled = true;
        var employee = new Employee { Email = "transition@example.invalid", Role = Employee.RoleEmployee };
        var booking = new Booking { EmployeeId = employee.Id.ToString(), Status = Booking.StatusConfirmed, StartUtc = Now.AddDays(1), EndUtc = Now.AddDays(1).AddHours(2) };
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { writer.Store(company); writer.Store(employee); writer.Store(booking); await writer.SaveChangesAsync(); }
        var fields = new EmployeeFields(employee.Email, "Property", "Owner", "", Employee.RolePartner, null, new(), null, null, null, null, null, company.Id);
        using (var session = fixture.Session())
            await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => fixture.Handler(session).Handle(new(employee.Id, fields, true), default));
        using (var reader = fixture.Store.QuerySession(fixture.Tenant)) Assert.Equal(Employee.RoleEmployee, (await reader.LoadAsync<Employee>(employee.Id))!.Role);
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { booking.Status = Booking.StatusCompleted; writer.Store(booking); await writer.SaveChangesAsync(); }
        using (var session = fixture.Session()) await fixture.Handler(session).Handle(new(employee.Id, fields, true), default);
        using (var reader = fixture.Store.QuerySession(fixture.Tenant))
        { Assert.Equal(Employee.RolePartner, (await reader.LoadAsync<Employee>(employee.Id))!.Role); Assert.NotNull(await reader.LoadAsync<Booking>(booking.Id)); }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Booking_assignment_racing_partner_transition_cannot_leave_a_partner_assigned_to_work()
    {
        using var fixture = await Fixture.Create();
        var company = Company.Create("test", "Owner"); company.IsPartnerEnabled = true;
        var employee = new Employee { Email = "race@example.invalid", Role = Employee.RoleEmployee };
        var booking = new Booking { Status = Booking.StatusConfirmed, StartUtc = Now.AddDays(1), EndUtc = Now.AddDays(1).AddHours(2) };
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant)) { writer.Store(company); writer.Store(employee); writer.Store(booking); await writer.SaveChangesAsync(); }
        var fields = new EmployeeFields(employee.Email, "Property", "Owner", "", Employee.RolePartner, null, new(), null, null, null, null, null, company.Id);
        using var changeSession = fixture.Session(); using var assignSession = fixture.Session();
        async Task<bool> Attempt(Func<Task> operation)
        {
            try { await operation(); return true; }
            catch (Exception error) when (error is EmployeeIdentityConflictException or CleaningSuite.Application.Bookings.SlotConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(
            Attempt(async () => { await fixture.Handler(changeSession).Handle(new(employee.Id, fields, true), default); }),
            Attempt(async () => { await new AssignBookingHandler(new BookingRepository(assignSession), new EmployeeRepository(assignSession), fixture.Reservation(assignSession))
                .Handle(new(booking.Id, employee.Id), default); }));
        Assert.Single(outcomes.Where(x => x));
        using var reader = fixture.Store.QuerySession(fixture.Tenant);
        var person = await reader.LoadAsync<Employee>(employee.Id); var work = await reader.LoadAsync<Booking>(booking.Id);
        Assert.False(person!.Role == Employee.RolePartner && work!.EmployeeId == employee.Id.ToString());
    }

    private sealed class Fixture(ServiceProvider services) : IDisposable
    {
        public IDocumentStore Store { get; } = services.GetRequiredService<IDocumentStore>();
        public string Tenant { get; } = "transition-" + Guid.NewGuid().ToString("N");
        public static async Task<Fixture> Create()
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "partner_transition_" + Guid.NewGuid().ToString("N"));
            var fixture = new Fixture(services.BuildServiceProvider()); await fixture.Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(); return fixture;
        }
        private ITenantContext Context() { var mock = new Mock<ITenantContext>(); mock.SetupGet(x => x.TenantId).Returns(Tenant); return mock.Object; }
        public TenantSessionFactory Session() => new(Store, Context());
        public PartnerWorkforceTransitionGuard Guard(ITenantSession session)
        {
            var profiles = new Mock<ITenantProfileRepository>();
            profiles.Setup(x => x.GetAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "UTC" });
            return new(new PartnerStore(session), new ShiftRepository(session), profiles.Object, Context(), new Clock());
        }
        public UpdateEmployeeHandler Handler(ITenantSession session)
        {
            var locks = new Mock<IEmployeeIdentityLock>(); locks.Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
            return new(new EmployeeRepository(session), Mock.Of<IEmployeeIdentityProvider>(), Context(), locks.Object,
                Reservation(session), new CompanyRepository(session), Guard(session));
        }
        public IShiftWorkReservation Reservation(ITenantSession session) => new ShiftWorkReservation(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING") }).Build(), session);
        public void Dispose() => services.Dispose();
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_key_request_still_to_complete_blocks_the_partner_transition_but_a_completed_one_does_not()
    {
        using var fixture = await Fixture.Create();
        var open = Guid.NewGuid(); var finished = Guid.NewGuid();
        using (var writer = fixture.Store.LightweightSession(fixture.Tenant))
        {
            writer.Store(new PartnerKeyRequest { CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(), Date = new DateOnly(2026, 11, 20), AssignedEmployeeId = open, Status = PartnerKeyRequest.StatusRequested });
            writer.Store(new PartnerKeyRequest { CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(), Date = new DateOnly(2026, 11, 2), AssignedEmployeeId = finished, Status = PartnerKeyRequest.StatusCompleted });
            await writer.SaveChangesAsync();
        }
        using var blocked = fixture.Session();
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => fixture.Guard(blocked).EnsureNoOutstandingWorkAsync(open, default));
        using var allowed = fixture.Session();
        await fixture.Guard(allowed).EnsureNoOutstandingWorkAsync(finished, default);
    }
}
