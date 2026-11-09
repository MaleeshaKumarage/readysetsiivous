using CleaningSuite.Application.Common;
using CleaningSuite.Application.Inventory;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Inventory;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using FluentValidation;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text.Json;

namespace CleaningSuite.Tests.Inventory;

public sealed class InventoryAcknowledgmentDatabaseFixture : IDisposable
{
    private ServiceProvider? provider;
    public string Connection => Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
    public IDocumentStore Store
    {
        get
        {
            if (provider is null)
            {
                var services = new ServiceCollection(); services.AddLogging();
                services.AddCleaningMarten(Connection, databaseSchemaName: "inventory_ack_" + Guid.NewGuid().ToString("N"));
                provider = services.BuildServiceProvider();
                provider.GetRequiredService<IDocumentStore>().Storage.ApplyAllConfiguredChangesToDatabaseAsync().GetAwaiter().GetResult();
            }
            return provider.GetRequiredService<IDocumentStore>();
        }
    }
    public void Dispose() => provider?.Dispose();
}

public sealed class InventoryAcknowledgmentDatabaseTests(InventoryAcknowledgmentDatabaseFixture fixture) : IClassFixture<InventoryAcknowledgmentDatabaseFixture>
{
    private sealed record Seed(string Tenant, Company Company, Employee Owner, Employee OtherOwner, InventoryUsage First, InventoryUsage Second);
    private async Task<Seed> SeedAsync()
    {
        var tenant = "ack-" + Guid.NewGuid().ToString("N");
        var company = Company.Create("1234567-8", "Synthetic partner"); company.IsPartnerEnabled = true;
        var other = Company.Create("8765432-1", "Other partner"); other.IsPartnerEnabled = true;
        var owner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = company.Id, FirstName = "Test", LastName = "Owner" };
        var otherOwner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = other.Id };
        var first = new InventoryUsage { EmployeeId = Guid.NewGuid(), EmployeeName = "Private Cleaner Name", ItemName = "Keys", LocationName = "Store", Quantity = 2, Unit = "pcs", Note = "Spare set", TakenUtc = DateTime.UtcNow, Photos = [new(Guid.NewGuid(), ".png")] };
        var second = new InventoryUsage { ItemName = "Bedsheets", LocationName = "Store", Quantity = 4, Unit = "pcs", TakenUtc = DateTime.UtcNow, Photos = [new(Guid.NewGuid(), ".png")] };
        using var db = fixture.Store.LightweightSession(tenant);
        db.Store(company); db.Store(other); db.Store(owner); db.Store(otherOwner); db.Store(first); db.Store(second);
        await db.SaveChangesAsync();
        return new(tenant, company, owner, otherOwner, first, second);
    }

    private (InventoryAcknowledgmentService Service, TenantSessionFactory Session) Service(string tenantId)
    {
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        var session = new TenantSessionFactory(fixture.Store, tenant.Object);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.Connection }).Build();
        var files = new Mock<IInventoryFileStore>();
        files.Setup(x => x.ReadAsync(It.IsAny<Guid>(), It.IsAny<InventoryPhoto>(), It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 1, 2, 3 });
        var inventory = new InventoryService(new InventoryRepository(session), files.Object, new Mock<IUploadContentValidator>().Object, TimeProvider.System);
        return (new(new PartnerStore(session), new ShiftWorkReservation(configuration, session), inventory, TimeProvider.System), session);
    }
    private static InventoryAcknowledgmentInput Input(Seed seed, params Guid[] ids) => new(Guid.NewGuid(), seed.Company.Id, ids, "Please confirm");

    [QualityCycleDatabaseFact]
    public async Task Batch_is_snapshotted_private_idempotent_and_visible_as_pending_notification()
    {
        var seed = await SeedAsync(); var (service, session) = Service(seed.Tenant); using var _ = session;
        var input = Input(seed, seed.First.Id, seed.Second.Id);
        var rows = await service.Create("admin-subject", input, default);
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows.Select(x => x.Id), (await service.Create("admin-subject", input, default)).Select(x => x.Id));
        using (var write = fixture.Store.LightweightSession(seed.Tenant))
        {
            var usage = (await write.LoadAsync<InventoryUsage>(seed.First.Id))!;
            usage.Note = "Later source change"; write.Store(usage); await write.SaveChangesAsync();
        }
        var page = await service.List(seed.Owner.Id, false, 0, default);
        Assert.Equal(2, page.PendingCount); Assert.False(page.HasMore);
        Assert.Equal("Spare set", page.Items.Single(x => x.Id == seed.First.Id).Note);
        var json = JsonSerializer.Serialize(page);
        Assert.DoesNotContain(seed.First.EmployeeName, json); Assert.DoesNotContain(seed.First.EmployeeId.ToString(), json);
        Assert.DoesNotContain("admin-subject", json);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await service.Photo(seed.Owner.Id, seed.First.Id, seed.First.Photos[0].Id, default)).Bytes);
        Assert.Equal(2, (await service.Lookup([seed.First.Id, seed.Second.Id], default)).Count);
    }

    [QualityCycleDatabaseFact]
    public async Task Invalid_batches_do_not_partially_share_and_expired_evidence_is_rejected()
    {
        var seed = await SeedAsync(); var (service, session) = Service(seed.Tenant); using var _ = session;
        await Assert.ThrowsAsync<NotFoundException>(() => service.Create("admin", Input(seed, seed.First.Id, Guid.NewGuid()), default));
        Assert.Empty(await service.Lookup([seed.First.Id], default));
        await Assert.ThrowsAsync<ValidationException>(() => service.Create("admin", Input(seed, seed.First.Id, seed.First.Id), default));
        await Assert.ThrowsAsync<ValidationException>(() => service.Create("admin", Input(seed) with { Message = new string('x', 501) }, default));
        using (var db = fixture.Store.LightweightSession(seed.Tenant))
        {
            var usage = (await db.LoadAsync<InventoryUsage>(seed.Second.Id))!;
            usage.TakenUtc = DateTime.UtcNow.AddMonths(-4); db.Store(usage); await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ValidationException>(() => service.Create("admin", Input(seed, seed.First.Id, seed.Second.Id), default));
        Assert.Empty(await service.Lookup([seed.First.Id], default));
    }

    [QualityCycleDatabaseFact]
    public async Task Company_tenant_identity_and_photo_boundaries_are_enforced()
    {
        var seed = await SeedAsync(); var (service, session) = Service(seed.Tenant); using var _ = session;
        var row = Assert.Single(await service.Create("admin", Input(seed, seed.First.Id), default));
        Assert.Empty((await service.List(seed.OtherOwner.Id, false, 0, default)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.Respond(seed.OtherOwner.Id, row.Id, new(row.Version, "Acknowledged", null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.Photo(seed.OtherOwner.Id, row.Id, seed.First.Photos[0].Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.Photo(seed.Owner.Id, row.Id, seed.Second.Photos[0].Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.List(Guid.NewGuid(), false, 0, default));
        var (foreign, foreignSession) = Service("other-tenant"); using var foreignLease = foreignSession;
        Assert.Empty(await foreign.Lookup([row.Id], default));
        await Assert.ThrowsAsync<ValidationException>(() => foreign.Create("admin", Input(seed, row.Id), default));
        using (var db = fixture.Store.LightweightSession(seed.Tenant))
        {
            var company = (await db.LoadAsync<Company>(seed.Company.Id))!; company.IsPartnerEnabled = false;
            db.Store(company); await db.SaveChangesAsync();
        }
        var (disabled, disabledSession) = Service(seed.Tenant); using var disabledLease = disabledSession;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => disabled.List(seed.Owner.Id, false, 0, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => disabled.Photo(seed.Owner.Id, row.Id, seed.First.Photos[0].Id, default));
    }

    [QualityCycleDatabaseFact]
    public async Task Response_is_audited_versioned_immutable_and_retry_safe_without_modifying_inventory()
    {
        var seed = await SeedAsync(); var (service, session) = Service(seed.Tenant); using var _ = session;
        var row = Assert.Single(await service.Create("admin", Input(seed, seed.First.Id), default));
        await Assert.ThrowsAsync<ValidationException>(() => service.Respond(seed.Owner.Id, row.Id, new(row.Version, "IssueReported", " "), default));
        await Assert.ThrowsAsync<ValidationException>(() => service.Respond(seed.Owner.Id, row.Id, new(row.Version, "Other", "note"), default));
        await Assert.ThrowsAsync<InventoryConflictException>(() => service.Respond(seed.Owner.Id, row.Id, new(Guid.NewGuid(), "Acknowledged", ""), default));
        var response = await service.Respond(seed.Owner.Id, row.Id, new(row.Version, "IssueReported", " Wrong key "), default);
        Assert.Equal("IssueReported", response.Status); Assert.Equal("Wrong key", response.ResponseNote);
        Assert.Equal("Test Owner", response.RespondedByName); Assert.NotNull(response.RespondedUtc);
        var retry = await service.Respond(seed.Owner.Id, row.Id, new(row.Version, "IssueReported", "Wrong key"), default);
        Assert.Equal(response.RespondedUtc, retry.RespondedUtc);
        await Assert.ThrowsAsync<InventoryConflictException>(() => service.Respond(seed.Owner.Id, row.Id, new(response.Version, "Acknowledged", ""), default));
        Assert.Equal(0, (await service.List(seed.Owner.Id, false, 0, default)).PendingCount);
        Assert.Equal("IssueReported", Assert.Single((await service.List(seed.Owner.Id, true, 0, default)).Items).Status);
        using var read = fixture.Store.QuerySession(seed.Tenant);
        var original = (await read.LoadAsync<InventoryUsage>(seed.First.Id))!;
        Assert.Equal(seed.First.Note, original.Note); Assert.Equal(seed.First.Quantity, original.Quantity);
        Assert.Equal(seed.Owner.Id, (await read.LoadAsync<InventoryAcknowledgment>(row.Id))!.RespondedByEmployeeId);
    }

    [QualityCycleDatabaseFact]
    public async Task Concurrent_overlapping_batches_cannot_duplicate_or_partially_share_entries()
    {
        var seed = await SeedAsync(); var (first, session1) = Service(seed.Tenant); using var _ = session1;
        var (second, session2) = Service(seed.Tenant); using var __ = session2;
        var tasks = new[] {
            first.Create("admin", Input(seed, seed.First.Id), default),
            second.Create("admin", Input(seed, seed.First.Id, seed.Second.Id), default),
        };
        try { await Task.WhenAll(tasks); } catch (InventoryConflictException) { }
        Assert.Equal(1, tasks.Count(task => task.IsCompletedSuccessfully));
        Assert.Equal(1, tasks.Count(task => task.IsFaulted));
        using var read = fixture.Store.QuerySession(seed.Tenant);
        var saved = await read.Query<InventoryAcknowledgment>().ToListAsync();
        var winner = tasks.Single(task => task.IsCompletedSuccessfully).Result;
        Assert.Equal(winner.Count, saved.Count); Assert.Single(saved.Select(x => x.BatchId).Distinct());
    }

    [QualityCycleDatabaseFact]
    public async Task Pending_count_covers_all_pages_and_history_is_separate()
    {
        var seed = await SeedAsync();
        using (var db = fixture.Store.LightweightSession(seed.Tenant))
        {
            for (var i = 0; i < 27; i++) db.Store(new InventoryAcknowledgment { CompanyId = seed.Company.Id, Status = "Pending" });
            db.Store(new InventoryAcknowledgment { CompanyId = seed.Company.Id, Status = "Acknowledged" });
            db.Store(new InventoryAcknowledgment { CompanyId = seed.OtherOwner.PartnerCompanyId!.Value, Status = "Pending" });
            await db.SaveChangesAsync();
        }
        var (service, session) = Service(seed.Tenant); using var _ = session;
        var page = await service.List(seed.Owner.Id, false, 0, default);
        Assert.Equal(27, page.PendingCount); Assert.Equal(25, page.Items.Count); Assert.True(page.HasMore);
        var next = await service.List(seed.Owner.Id, false, 25, default);
        Assert.Equal(2, next.Items.Count); Assert.False(next.HasMore);
        Assert.Empty(page.Items.Select(x => x.Id).Intersect(next.Items.Select(x => x.Id)));
        Assert.Single((await service.List(seed.Owner.Id, true, 0, default)).Items);
    }

    [QualityCycleDatabaseFact]
    public async Task Partner_search_is_bounded_searchable_and_excludes_disabled_companies()
    {
        var seed = await SeedAsync();
        using (var db = fixture.Store.LightweightSession(seed.Tenant))
        {
            for (var i = 0; i < 30; i++)
            {
                var company = Company.Create($"synthetic-{i}", $"Partner {i:00}"); company.IsPartnerEnabled = true; db.Store(company);
            }
            var disabled = Company.Create("synthetic-disabled", "Disabled partner"); db.Store(disabled);
            await db.SaveChangesAsync();
        }
        var (service, session) = Service(seed.Tenant); using var _ = session;
        Assert.Equal(25, (await service.Partners(null, default)).Count);
        Assert.Equal("Partner 29", Assert.Single(await service.Partners("pArTnEr 29", default)).Name);
        Assert.Empty(await service.Partners("Disabled", default));
    }

    [QualityCycleDatabaseFact]
    public async Task Photo_retention_does_not_remove_acknowledgment_history_or_allow_reading_deleted_photos()
    {
        var seed = await SeedAsync(); var (service, session) = Service(seed.Tenant); using var _ = session;
        await service.Create("admin", Input(seed, seed.First.Id), default);
        using (var db = fixture.Store.LightweightSession(seed.Tenant))
        {
            var usage = (await db.LoadAsync<InventoryUsage>(seed.First.Id))!;
            usage.Photos = []; usage.PhotosPurgedUtc = DateTime.UtcNow; db.Store(usage); await db.SaveChangesAsync();
        }
        var (fresh, freshSession) = Service(seed.Tenant); using var freshLease = freshSession;
        Assert.Single((await fresh.List(seed.Owner.Id, false, 0, default)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => fresh.Photo(seed.Owner.Id, seed.First.Id, seed.First.Photos[0].Id, default));
    }
}
