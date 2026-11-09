using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Partners;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using FluentValidation;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Partners;

[Collection("Database integration")]
public class PartnerKeyRequestDatabaseTests
{
    private sealed record Fixture(PartnerKeyRequestService Service, Employee Partner, Employee OtherPartner, Employee Manager, Branch Branch, TenantProfile Profile, Mock<ITenantProfileRepository> Profiles, IDocumentStore Store, string TenantId);

    private static async Task<Fixture> Create()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(connection, databaseSchemaName: "partner_keys_" + Guid.NewGuid().ToString("N"));
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>(); await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenantId = "partner-keys-" + Guid.NewGuid().ToString("N");
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        var company = Company.Create("1234567-8", "Synthetic owner"); company.IsPartnerEnabled = true;
        var otherCompany = Company.Create("7654321-0", "Synthetic other owner"); otherCompany.IsPartnerEnabled = true;
        var partner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = company.Id, IsActive = true };
        var otherPartner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = otherCompany.Id, IsActive = true };
        var manager = new Employee { Role = Employee.RoleManager, IsActive = true };
        var branch = new Branch { CompanyId = company.Id, Name = "Synthetic lake flat", IsActive = true };
        using (var seed = store.LightweightSession(tenantId))
        {
            seed.Store(company); seed.Store(otherCompany); seed.Store(partner); seed.Store(otherPartner); seed.Store(manager); seed.Store(branch);
            await seed.SaveChangesAsync();
        }
        var profile = new TenantProfile { Slug = "synthetic", KeyRequestFeeNet = 15m, DefaultVatRatePercent = 25.5m };
        var profiles = new Mock<ITenantProfileRepository>();
        profiles.Setup(p => p.GetAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        profiles.Setup(p => p.SaveAsync(tenantId, It.IsAny<TenantProfile>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:DefaultConnection", connection } }).Build();
        var session = new TenantSessionFactory(store, tenant.Object);
        var service = new PartnerKeyRequestService(new PartnerStore(session), new ShiftWorkReservation(config, session), profiles.Object, tenant.Object);
        return new(service, partner, otherPartner, manager, branch, profile, profiles, store, tenantId);
    }

    [QualityCycleDatabaseFact]
    public async Task A_partner_pickup_request_freezes_the_current_fee_and_retries_idempotently()
    {
        var f = await Create();
        var created = await f.Service.Create(f.Partner.Id, new(Guid.NewGuid(), f.Branch.Id, "Pickup", new DateOnly(2026, 12, 1), "Lockbox 1234"), default);

        Assert.Equal("Pickup", created.Kind);
        Assert.Equal("Requested", created.Status);
        Assert.Equal(15m, created.FeeNet);
        Assert.Equal(25.5m, created.VatRatePercent);
        Assert.Equal("Synthetic lake flat", created.BranchName);

        // A retry with the same client request identifier returns the stored request, not a duplicate.
        var clientId = Guid.NewGuid();
        var first = await f.Service.Create(f.Partner.Id, new(clientId, f.Branch.Id, "Handover", new DateOnly(2026, 12, 2)), default);
        var retry = await f.Service.Create(f.Partner.Id, new(clientId, f.Branch.Id, "Handover", new DateOnly(2026, 12, 2)), default);
        Assert.Equal(first.Id, retry.Id);
        var list = await f.Service.List(f.Partner.Id, false, null, default);
        Assert.Equal(2, list.Items.Count);
    }

    [QualityCycleDatabaseFact]
    public async Task A_later_fee_change_does_not_rewrite_requests_already_submitted()
    {
        var f = await Create();
        var created = await f.Service.Create(f.Partner.Id, new(Guid.NewGuid(), f.Branch.Id, "Pickup", new DateOnly(2026, 12, 1)), default);

        f.Profile.KeyRequestFeeNet = 40m;
        var list = await f.Service.List(f.Partner.Id, false, null, default);

        Assert.Equal(40m, list.FeeNet);
        Assert.Equal(15m, list.Items.Single(x => x.Id == created.Id).FeeNet);
    }

    [QualityCycleDatabaseFact]
    public async Task Missing_or_unknown_fields_are_named_together()
    {
        var f = await Create();
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service.Create(f.Partner.Id, new(Guid.Empty, Guid.Empty, "Repaint", default), default));
        var fields = ex.Errors.Select(e => e.PropertyName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "BranchId", "ClientRequestId", "Date", "Kind" }, fields);
    }

    [QualityCycleDatabaseFact]
    public async Task Cancelling_needs_a_reason_the_current_revision_and_the_owning_company()
    {
        var f = await Create();
        var created = await f.Service.Create(f.Partner.Id, new(Guid.NewGuid(), f.Branch.Id, "Pickup", new DateOnly(2026, 12, 1)), default);

        await Assert.ThrowsAsync<ValidationException>(() => f.Service.Cancel(f.Partner.Id, created.Id, 1, "   ", default));
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Service.Cancel(f.Partner.Id, created.Id, 7, "Changed plans", default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.Cancel(f.OtherPartner.Id, created.Id, 1, "Not theirs", default));

        var cancelled = await f.Service.Cancel(f.Partner.Id, created.Id, 1, "Changed plans", default);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Changed plans", cancelled.CancellationReason);
        Assert.Equal(2, cancelled.Revision);
    }

    [QualityCycleDatabaseFact]
    public async Task Only_managers_set_the_fee_and_it_rejects_negative_amounts()
    {
        var f = await Create();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.SetFee(f.Partner.Id, new(20m), default));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.SetFee(f.Manager.Id, new(-1m), default));

        var list = await f.Service.SetFee(f.Manager.Id, new(22.5m), default);
        Assert.Equal(22.5m, list.FeeNet);
        f.Profiles.Verify(p => p.SaveAsync(f.TenantId, It.IsAny<TenantProfile>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
