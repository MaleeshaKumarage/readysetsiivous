using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.WorkQueue;

/// <summary>Runs the merged work queue query against PostgreSQL, including case-insensitive search.</summary>
public class WorkQueueReaderDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Search_is_case_insensitive_across_bookings_and_partner_companies()
    {
        using var f = await Fixture.Create();
        var lake = new Company { Name = "Lake Owners Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var forest = new Company { Name = "Forest Homes", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var start = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(lake, forest);
            db.Store(new Booking { BookingNumber = "BK-1001", Customer = new Customer { Name = "Anna Virtanen" }, Status = Booking.StatusNew, StartUtc = start, StartLocalDate = "2026-12-01", StartLocalTime = "10:00" });
            db.Store(new Booking { BookingNumber = "BK-2002", Customer = new Customer { Name = "Ville Lakeside" }, Status = Booking.StatusNew, StartUtc = start.AddDays(1), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusOffered, FromDate = new DateOnly(2026, 12, 3), ToDate = new DateOnly(2026, 12, 3) });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = forest.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusRequested, FromDate = new DateOnly(2026, 12, 4), ToDate = new DateOnly(2026, 12, 4) });
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = new WorkQueueReader(session);

        var all = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, 0, 25));
        Assert.Equal(4, all.Total);

        // "LAKE" matches the Lake Owners partner request and the "Lakeside" booking customer, regardless of case.
        var lakes = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", "LAKE", 0, 25));
        Assert.Equal(2, lakes.Total);
        Assert.Equal(["BK-2002", "Lake Owners Oy"], lakes.Items.Select(i => i.Source == "booking" ? i.Reference : i.CustomerName).Order().ToArray());
        Assert.Equal("Awaiting acceptance", Assert.Single(lakes.Items, i => i.Source == "partner").StateLabel);

        var byNumber = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "booking", "bk-1001", 0, 25));
        Assert.Equal("Anna Virtanen", Assert.Single(byNumber.Items).CustomerName);
    }

    private sealed class Fixture : IDisposable
    {
        public required ServiceProvider Provider; public required IDocumentStore Store;
        public string Tenant = "work-queue-" + Guid.NewGuid().ToString("N");
        public static async Task<Fixture> Create()
        {
            var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
            var services = new ServiceCollection(); services.AddLogging();
            services.AddCleaningMarten(connection, databaseSchemaName: "work_queue_" + Guid.NewGuid().ToString("N"));
            var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            return new Fixture { Provider = provider, Store = store };
        }
        public TenantSessionFactory Session() { var context = new Mock<ITenantContext>(); context.SetupGet(t => t.TenantId).Returns(Tenant); return new(Store, context.Object); }
        public void Dispose() => Provider.Dispose();
    }
}
