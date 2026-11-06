using System.Diagnostics;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Notifications;
using CleaningSuite.Infrastructure.Partners;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Scale;

/// <summary>
/// Seeds the target scale (50 companies, 500 shifts, 100 employees, 5 partner companies with 100 properties, 300 bookings)
/// and times the hot paths against real PostgreSQL. Results are checked for correctness, not only speed.
/// Set SCALE_REPORT_PATH to write the timings to a file.
/// </summary>
[Collection(ScaleCollection.Name)]
public class ScaleUsabilityDatabaseTests
{
    // Friday 2026-11-06 15:45 Helsinki is 13:45 UTC: the 16:00 Friday visits are due for their Upcoming reminder.
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 13, 45, 0, TimeSpan.Zero);
    private const int CompanyCount = 50, PartnerCount = 5, ShiftCount = 500, EmployeeCount = 100, BookingCount = 300;
    private const int AssignedShiftEvery = 2; // every second shift has an employee; the rest are unassigned.
    // Fridays in October 2026, the month billed below.
    private static readonly int[] OctoberFridays = [2, 9, 16, 23, 30];

    [QualityCycleDatabaseFact]
    public async Task Target_scale_hot_paths_return_correct_results_within_usable_time()
    {
        var tenant = "scale-" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "scale_" + Guid.NewGuid().ToString("N"));
        var context = new Mock<ITenantContext>(); context.SetupGet(t => t.TenantId).Returns(tenant);
        services.AddSingleton(context.Object);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build());
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddScoped<ITenantSession, TenantSessionFactory>();
        services.AddScoped<ITenantProfileRepository, TenantProfileRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IQualityCycleRepository, QualityCycleRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IShiftBillingRepository, ShiftBillingRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IShiftWorkReservation, ShiftWorkReservation>();
        services.AddScoped<IPartnerStore, PartnerStore>();
        services.AddScoped<IPartnerInvoiceSource, PartnerInvoiceSource>();
        services.AddScoped<IPushOutbox, PushOutbox>();
        services.AddScoped<PushReminderScheduler>();
        services.AddScoped<ShiftInvoiceService>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var seeded = await SeedAsync(store, tenant);
        var timings = new List<string>();
        async Task<T> Timed<T>(string name, Func<Task<T>> action, int limitMs)
        {
            var clock = Stopwatch.StartNew();
            var result = await action();
            clock.Stop();
            timings.Add($"{name}: {clock.ElapsedMilliseconds} ms (limit {limitMs} ms)");
            Assert.True(clock.ElapsedMilliseconds < limitMs, $"{name} took {clock.ElapsedMilliseconds} ms, over the {limitMs} ms usability limit.");
            return result;
        }

        // 1. Work queue: needs-action page 1 and a deep page, across all sources.
        using (var scope = provider.CreateScope())
        {
            var reader = new WorkQueueReader(scope.ServiceProvider.GetRequiredService<ITenantSession>(), scope.ServiceProvider.GetRequiredService<ITenantProfileRepository>(), context.Object);
            var first = await Timed("work queue needs-action page 1", () => reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, 0, 25)), 5000);
            Assert.Equal(25, first.Items.Count);
            Assert.True(first.Counts[WorkQueueViews.NeedsAction] >= ShiftCount / AssignedShiftEvery, "needs-action count is missing unassigned visits");
            await Timed("work queue needs-action deep page", () => reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, 200, 25)), 5000);
            await Timed("work queue search", () => reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", "Company 07", 0, 25)), 5000);
        }

        // 2. Shift list used by the admin shifts and invoice pages: every shift must come back.
        using (var scope = provider.CreateScope())
        {
            var shifts = scope.ServiceProvider.GetRequiredService<IShiftRepository>();
            var all = await Timed("admin shift list (all shifts)", () => shifts.ListIncludingInactiveAsync(default).ContinueWith(t => t.Result.ToList()), 5000);
            Assert.Equal(ShiftCount, all.Count);
        }

        // 3. Reminder scan: runs every minute per tenant, across every shift. Each assigned visit due for a reminder is staged once.
        using (var scope = provider.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<PushReminderScheduler>();
            await Timed("reminder scan (first run)", () => scheduler.ScheduleAsync().ContinueWith(_ => 0), 10000);
        }
        using (var scope = provider.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<PushReminderScheduler>();
            await Timed("reminder scan (repeat run, nothing new)", () => scheduler.ScheduleAsync().ContinueWith(_ => 0), 10000);
        }
        using (var query = store.QuerySession(tenant))
        {
            var staged = await query.Query<PushIntent>().Where(x => x.Kind == "Upcoming").CountAsync();
            Assert.Equal(seeded.AssignedVisitsDueSoon, staged);
            timings.Add($"reminders staged for assigned visits due now: {staged} (expected {seeded.AssignedVisitsDueSoon})");
        }

        // 4. Invoice preview for one partner company with all of its shifts for October: correct lines and unstarted visits.
        using (var scope = provider.CreateScope())
        {
            var invoices = scope.ServiceProvider.GetRequiredService<ShiftInvoiceService>();
            var request = new ShiftInvoiceRequest(seeded.FirstCompany, seeded.CompanyShifts[seeded.FirstCompany].ToArray(), new(2026, 10, 1), new(2026, 10, 31));
            var preview = await Timed("invoice preview, 10 shifts for one month", () => invoices.PreviewAsync(request, default), 10000);
            Assert.Equal(seeded.CompanyShifts[seeded.FirstCompany].Count, preview.Invoice.Lines.Count);
            Assert.Equal(seeded.ExpectedUnstartedForFirstCompany, preview.UnstartedVisits!.Count);
            timings.Add($"invoice lines {preview.Invoice.Lines.Count}, unstarted visits listed {preview.UnstartedVisits.Count} (expected {seeded.ExpectedUnstartedForFirstCompany})");
        }

        var report = Environment.GetEnvironmentVariable("SCALE_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(report)) await File.WriteAllLinesAsync(report, timings);
        foreach (var line in timings) Console.WriteLine(line);
    }

    /// <summary>
    /// Local-only: seeds the target volume into an existing tenant in the default schema, so the running API and browser
    /// can use it with a real login. Runs only when SCALE_SEED_LOCAL_TENANT and SCALE_SEED_CONNECTION are set.
    /// </summary>
    [Fact]
    public async Task Seed_target_scale_into_local_tenant_for_real_login_checks()
    {
        var tenant = Environment.GetEnvironmentVariable("SCALE_SEED_LOCAL_TENANT");
        var connection = Environment.GetEnvironmentVariable("SCALE_SEED_CONNECTION");
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(connection)) return;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var seeded = await SeedAsync(store, tenant, storeProfile: false);
        Console.WriteLine($"Seeded tenant {tenant}: first company {seeded.FirstCompany}, expected unstarted {seeded.ExpectedUnstartedForFirstCompany}");
    }

    private sealed record Seeded(Guid FirstCompany, Dictionary<Guid, List<Guid>> CompanyShifts, int AssignedVisitsDueSoon, int ExpectedUnstartedForFirstCompany);

    private static async Task<Seeded> SeedAsync(IDocumentStore store, string tenant, bool storeProfile = true)
    {
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var profile = new TenantProfile { Id = TenantDocumentIds.TenantProfile(tenant), Slug = tenant, RealmName = tenant, CompanyName = "ReadySet Scale Oy",
            BusinessId = "9999999-9", TimeZoneId = "Europe/Helsinki", CompanyAddress = new() { Street = "Street 1", PostalCode = "00100", City = "Helsinki" },
            BankAccountIBAN = "FI2112345600000785", BankBic = "NDEAFIHH", PaymentTermsDays = 14 };
        var companies = new List<Company>(); var branches = new List<Branch>(); var shifts = new List<Shift>(); var pricing = new List<ShiftBillingProfile>();
        var assignments = new List<ShiftAssignment>(); var forms = new List<QualityCycleForm>(); var bookings = new List<Booking>();
        var employees = new List<Employee>(); var devices = new List<PushDevice>();
        var companyShifts = new Dictionary<Guid, List<Guid>>();

        for (var i = 0; i < EmployeeCount; i++)
        {
            var employee = new Employee { FirstName = "Cleaner", LastName = $"{i:000}", Role = Employee.RoleEmployee, IsActive = true, KeycloakUserId = $"sub-{i}" };
            employees.Add(employee);
            devices.Add(new PushDevice { EmployeeId = employee.Id, Subject = $"sub-{i}", Enabled = true, OptedInUtc = Now.UtcDateTime.AddDays(-30), CreatedUtc = Now.UtcDateTime.AddDays(-30) });
        }

        // Five partner companies own 100 properties in total (20 each); the other 45 companies have one property each.
        var branchesByCompany = new Dictionary<int, List<Branch>>();
        for (var c = 0; c < CompanyCount; c++)
        {
            var partner = c < PartnerCount;
            var company = new Company { Name = $"Company {c:00} Oy", BusinessId = $"{1000000 + c}-1", IsActive = true, IsPartnerEnabled = partner, ContactEmail = $"c{c}@example.test" };
            companies.Add(company);
            var propertyCount = partner ? 20 : 1;
            branchesByCompany[c] = [];
            for (var p = 0; p < propertyCount; p++)
            {
                var branch = new Branch { CompanyId = company.Id, Name = $"Property {c:00}-{p:00}", IsActive = true };
                branches.Add(branch); branchesByCompany[c].Add(branch);
            }
        }

        var shiftIndex = 0;
        for (var s = 0; s < ShiftCount; s++)
        {
            var c = s % CompanyCount; var company = companies[c]; var branch = branchesByCompany[c][s / CompanyCount % branchesByCompany[c].Count];
            var shift = new Shift { CompanyId = company.Id, BranchId = branch.Id, Name = $"Weekly {s:000}", IsActive = true, ValidFrom = new(2026, 10, 1),
                CreatedUtc = start.AddDays(-7), Schedule = new() { Type = ShiftScheduleType.Weekly, WeeklyDay = DayOfWeek.Friday, WeeklyStart = TimeSpan.FromHours(16), WeeklyEnd = TimeSpan.FromHours(18) } };
            shifts.Add(shift);
            pricing.Add(new ShiftBillingProfile { Id = shift.Id, ShiftId = shift.Id, Plans = [new("Monthly", 150, 25.5m, new(2026, 10, 1))] });
            if (!companyShifts.TryGetValue(company.Id, out var list)) companyShifts[company.Id] = list = [];
            list.Add(shift.Id);
            var employee = employees[s % EmployeeCount];
            if (s % AssignedShiftEvery == 0)
                assignments.Add(new ShiftAssignment { ShiftId = shift.Id, EmployeeId = employee.Id, IsActive = true, AssignedAtUtc = start.AddDays(-20) });
            // Half of October's visits are started and submitted, so the rest are unstarted.
            for (var d = 0; d < OctoberFridays.Length; d++)
            {
                if ((s + d) % 2 != 0) continue;
                // 16:00 Helsinki is 13:00 UTC on summer time (until 25 Oct) and 14:00 UTC after it.
                var occurrence = new DateTime(2026, 10, OctoberFridays[d], OctoberFridays[d] > 25 ? 14 : 13, 0, 0, DateTimeKind.Utc);
                forms.Add(new QualityCycleForm { Token = Guid.NewGuid().ToString("N"), ShiftId = shift.Id, EmployeeId = employee.Id, IsSubmitted = true, SubmittedUtc = occurrence.AddHours(2),
                    ShiftOccurrenceUtc = occurrence, StartedAtUtc = occurrence, EndedAtUtc = occurrence.AddHours(2) });
            }
            shiftIndex++;
        }
        Assert.Equal(ShiftCount, shiftIndex);

        for (var b = 0; b < BookingCount; b++)
        {
            var when = Now.UtcDateTime.AddDays(1 + b % 30).AddHours(9);
            bookings.Add(new Booking { BookingNumber = $"BK-{10000 + b}", Customer = new Customer { Name = $"Customer {b}" }, Status = Booking.StatusNew,
                StartUtc = when, StartLocalDate = when.ToString("yyyy-MM-dd"), StartLocalTime = "11:00" });
        }

        // Each assigned visit due for its Upcoming reminder at 13:45 UTC is the 2026-11-06 Friday 14:00 UTC occurrence.
        var assignedDue = shifts.Count(x => assignments.Any(a => a.ShiftId == x.Id));

        // Expected unstarted visits for the first company: any October Friday visit with no form in odd positions.
        var firstCompany = companies[0].Id;
        var firstShiftIndexes = Enumerable.Range(0, ShiftCount).Where(s => s % CompanyCount == 0).ToArray();
        var expectedUnstarted = firstShiftIndexes.Sum(s => Enumerable.Range(0, OctoberFridays.Length).Count(d => (s + d) % 2 != 0));

        using (var db = store.LightweightSession(tenant))
        {
            // An existing tenant keeps its own profile; storing a second copy would conflict on its version.
            if (storeProfile) db.Store(profile);
            foreach (var chunk in companies.Chunk(500)) db.Store(chunk);
            foreach (var chunk in branches.Chunk(500)) db.Store(chunk);
            foreach (var chunk in employees.Chunk(500)) db.Store(chunk);
            foreach (var chunk in devices.Chunk(500)) db.Store(chunk);
            foreach (var chunk in shifts.Chunk(500)) db.Store(chunk);
            foreach (var chunk in pricing.Chunk(500)) db.Store(chunk);
            foreach (var chunk in assignments.Chunk(500)) db.Store(chunk);
            foreach (var chunk in forms.Chunk(500)) db.Store(chunk);
            foreach (var chunk in bookings.Chunk(500)) db.Store(chunk);
            await db.SaveChangesAsync();
        }
        return new Seeded(firstCompany, companyShifts, assignedDue, expectedUnstarted);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
