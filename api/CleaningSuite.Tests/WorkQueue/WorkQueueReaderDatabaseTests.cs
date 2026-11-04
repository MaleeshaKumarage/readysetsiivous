using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
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
    public async Task Finished_partner_visit_is_completed_and_cancelled_visits_stay_out_of_completed()
    {
        using var f = await Fixture.Create();
        var lake = new Company { Name = "Lake Owners Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var shift = Guid.NewGuid();
        var finished = new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusAccepted,
            LinkedShiftId = shift, FromDate = new DateOnly(2026, 12, 1), ToDate = new DateOnly(2026, 12, 1) };
        var cancelled = new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusCancelled,
            FromDate = new DateOnly(2026, 12, 2), ToDate = new DateOnly(2026, 12, 2) };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(lake, finished, cancelled);
            db.Store(new QualityCycleForm { ShiftId = shift, EmployeeId = Guid.NewGuid(), IsSubmitted = true,
                StartedAtUtc = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc), EndedAtUtc = new DateTime(2026, 12, 1, 9, 30, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = new WorkQueueReader(session);
        var completed = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Completed, "partner", null, 0, 25));
        Assert.Equal(finished.Id, Assert.Single(completed.Items).Id);
        Assert.Equal("Completed", Assert.Single(completed.Items).StateLabel);
        var cancelledPage = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Cancelled, "partner", null, 0, 25));
        Assert.Equal(cancelled.Id, Assert.Single(cancelledPage.Items).Id);
        var scheduled = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "partner", null, 0, 25));
        Assert.Empty(scheduled.Items);
        Assert.Equal(1, completed.Counts[WorkQueueViews.Completed]);
        Assert.Equal(1, completed.Counts[WorkQueueViews.Cancelled]);
    }

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

    [QualityCycleDatabaseFact]
    public async Task All_sources_merge_on_local_time_and_a_huge_skip_returns_an_empty_page()
    {
        using var f = await Fixture.Create();
        var company = new Company { Name = "Merge Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            // 01:00 Helsinki on 3 Dec is 23:00 UTC on 2 Dec. A UTC key would wrongly put it before the 3 Dec partner request.
            db.Store(new Booking { BookingNumber = "BK-EARLY", Customer = new Customer { Name = "Early bird" }, Status = Booking.StatusNew,
                StartUtc = new DateTime(2026, 12, 2, 23, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-03", StartLocalTime = "01:00" });
            db.Store(new Booking { BookingNumber = "BK-DAY2", Customer = new Customer { Name = "Day two" }, Status = Booking.StatusNew,
                StartUtc = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = company.Id, BranchId = Guid.NewGuid(),
                Status = PartnerCleaningRequest.StatusRequested, FromDate = new DateOnly(2026, 12, 3), ToDate = new DateOnly(2026, 12, 3) });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = new WorkQueueReader(session);

        var page = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, 0, 25));
        Assert.Equal(["2026-12-02 10:00", "2026-12-03", "2026-12-03 01:00"], page.Items.Select(i => i.ScheduledLocal).ToArray());

        var beyond = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, int.MaxValue, 100));
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.Total);
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
