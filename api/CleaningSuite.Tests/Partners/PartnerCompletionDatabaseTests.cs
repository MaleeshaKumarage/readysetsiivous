using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text.Json;

namespace CleaningSuite.Tests.Partners;

// Exercises PartnerService.Completion against real documents: visit matching, completion gating and company scope.
public class PartnerCompletionDatabaseTests
{
    private static readonly DateTime Start = new(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);

    [QualityCycleDatabaseFact]
    public async Task Owner_sees_the_finished_visit_with_the_cleaner_by_initial_and_only_messages_about_it()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        using var provider = StoreProvider(connection, out var store);
        var tenantId = "partner-completion-" + Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(store, tenantId);
        var (service, session) = ServiceFor(store, connection, tenantId);
        using var _ = session;

        var completion = await service.Completion(seeded.Owner.Id, seeded.Request.Id, default);

        Assert.Equal("Apartment", completion.BranchName);
        Assert.Equal("T.", completion.CleanerInitial);
        var report = Assert.Single(completion.Reports);
        Assert.Equal(new[] { "Vacuum floors", "Restock towels" }, report.Tasks.Select(task => task.Text));
        Assert.Equal(new[] { true, false }, report.Tasks.Select(task => task.Done));
        Assert.Equal("Bathroom done", report.CleanerNotes);
        Assert.Equal("Kitchen after clean", Assert.Single(report.Photos).Note);
        Assert.Equal("Use the back door", Assert.Single(completion.Messages).Message);

        // Nothing identifying the cleaner leaves the partner view.
        var json = JsonSerializer.Serialize(completion, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.DoesNotContain("Test Cleaner", json);
        Assert.DoesNotContain(seeded.Cleaner.Id.ToString(), json);
    }

    [QualityCycleDatabaseFact]
    public async Task Request_list_shows_the_cleaner_by_initial_to_the_partner_but_by_full_name_to_staff()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        using var provider = StoreProvider(connection, out var store);
        var tenantId = "partner-mask-" + Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(store, tenantId);
        var (service, session) = ServiceFor(store, connection, tenantId);
        using var _ = session;

        var partnerView = await service.Get(seeded.Owner.Id, seeded.Request.Id, default);
        var staffView = await service.Requests(seeded.Staff.Id, true, null, default);

        Assert.Equal("T.", partnerView.AcceptedCleanerName);
        Assert.Equal("Test Cleaner", staffView.Single(item => item.Id == seeded.Request.Id).AcceptedCleanerName);
    }

    [QualityCycleDatabaseFact]
    public async Task Editing_request_instructions_updates_the_shift_the_cleaner_sees()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        using var provider = StoreProvider(connection, out var store);
        var tenantId = "partner-instructions-" + Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(store, tenantId, finishedForm: false, started: false);
        var (service, session) = ServiceFor(store, connection, tenantId);
        using var _ = session;

        await service.StaffEdit(seeded.Staff.Id, seeded.Request.Id,
            new EditPartnerRequest(seeded.Request.Revision, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1), null, "Use the back door"), default);

        using var read = store.QuerySession(tenantId);
        Assert.Equal("Use the back door", (await read.LoadAsync<Shift>(seeded.Request.LinkedShiftId!.Value))!.Notes);
    }

    [QualityCycleDatabaseFact]
    public async Task A_visit_that_is_not_finished_has_no_completion_summary()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        using var provider = StoreProvider(connection, out var store);
        var tenantId = "partner-unfinished-" + Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(store, tenantId, finishedForm: false);
        var (service, session) = ServiceFor(store, connection, tenantId);
        using var _ = session;

        var ex = await Assert.ThrowsAsync<ShiftConflictException>(() => service.Completion(seeded.Owner.Id, seeded.Request.Id, default));
        Assert.Equal("This visit has not been completed yet.", ex.Message);
    }

    [QualityCycleDatabaseFact]
    public async Task Another_company_cannot_open_the_visit()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        using var provider = StoreProvider(connection, out var store);
        var tenantId = "partner-scope-" + Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(store, tenantId);
        var (service, session) = ServiceFor(store, connection, tenantId);
        using var _ = session;

        await Assert.ThrowsAsync<NotFoundException>(() => service.Completion(seeded.OtherOwner.Id, seeded.Request.Id, default));
    }

    private sealed record Seeded(Employee Owner, Employee OtherOwner, Employee Staff, Employee Cleaner, PartnerCleaningRequest Request);

    private static ServiceProvider StoreProvider(string connection, out IDocumentStore store)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "partner_completion_" + Guid.NewGuid().ToString("N"));
        var provider = services.BuildServiceProvider();
        store = provider.GetRequiredService<IDocumentStore>();
        store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().GetAwaiter().GetResult();
        return provider;
    }

    private static (PartnerService Service, TenantSessionFactory Session) ServiceFor(IDocumentStore store, string connection, string tenantId)
    {
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:DefaultConnection", connection } }).Build();
        var session = new TenantSessionFactory(store, tenant.Object);
        var service = new PartnerService(new PartnerStore(session), new ShiftWorkReservation(config, session), new ShiftRepository(session), new Mock<ITenantProfileRepository>().Object, tenant.Object);
        return (service, session);
    }

    private static async Task<Seeded> SeedAsync(IDocumentStore store, string tenantId, bool finishedForm = true, bool started = true)
    {
        var company = Company.Create("1234567-8", "Synthetic owner"); company.IsPartnerEnabled = true;
        var otherCompany = Company.Create("7654321-0", "Other synthetic owner"); otherCompany.IsPartnerEnabled = true;
        var branch = Branch.Create(company.Id, "Apartment");
        var owner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = company.Id };
        var otherOwner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = otherCompany.Id };
        var staff = new Employee { Role = Employee.RoleAdmin, FirstName = "Ada", LastName = "Admin", IsActive = true };
        var cleaner = new Employee { FirstName = "Test", LastName = "Cleaner" };
        var shiftId = Guid.NewGuid();
        var request = new PartnerCleaningRequest
        {
            CompanyId = company.Id, BranchId = branch.Id, RequestedByEmployeeId = owner.Id, ClientRequestId = Guid.NewGuid(),
            FromDate = new DateOnly(2026, 12, 1), ToDate = new DateOnly(2026, 12, 1), EstimatedDurationMinutes = 120,
            PriceNet = 70, VatRatePercent = 25.5m, PricingVersion = 1, Status = PartnerCleaningRequest.StatusAccepted,
            LinkedShiftId = shiftId, AcceptedEmployeeId = cleaner.Id, PlannedStartUtc = Start, PlannedEndUtc = Start.AddMinutes(120),
            FirstAcceptedAtUtc = Start.AddDays(-1),
        };
        var form = QualityCycleForm.Create(shiftId, "Cleaning", cleaner.Id, "Test Cleaner", Guid.NewGuid(), "Apartment checklist", Start, ["Vacuum floors", "Restock towels"]);
        if (started) form.Start(Start.AddMinutes(5));
        if (finishedForm)
        {
            var items = form.Items.Select((item, index) => new QualityCycleFormItem { ItemText = item.ItemText, IsChecked = index == 0 }).ToList();
            form.Submit(items, null, "Bathroom done", [new QualityCyclePhoto("data:image/png;base64,AAAA", "Kitchen after clean")], Start.AddMinutes(110));
        }
        var message = new PartnerChangeRequest { CompanyId = company.Id, BranchId = branch.Id, RequestId = request.Id, RequestedByEmployeeId = owner.Id, Kind = "CleaningRequest", Message = "Use the back door" };
        var unrelated = new PartnerChangeRequest { CompanyId = company.Id, BranchId = branch.Id, RequestId = Guid.NewGuid(), RequestedByEmployeeId = owner.Id, Kind = "CleaningRequest", Message = "About another visit" };

        var shift = Shift.Create(company.Id, branch.Id, "Cleaning — Apartment", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible },
            "Old instructions", new DateTime(2026, 12, 1), new DateTime(2026, 12, 1));
        shift.Id = shiftId;

        using var seed = store.LightweightSession(tenantId);
        seed.Store(shift); seed.Store(company); seed.Store(otherCompany); seed.Store(branch);
        seed.Store(owner); seed.Store(otherOwner); seed.Store(staff); seed.Store(cleaner);
        seed.Store(request); seed.Store(form); seed.Store(message); seed.Store(unrelated);
        await seed.SaveChangesAsync();
        return new Seeded(owner, otherOwner, staff, cleaner, request);
    }
}
