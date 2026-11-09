using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
namespace CleaningSuite.Tests.Partners;

[Collection("Database integration")]
public class PartnerCompanyLifecycleDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Company_and_branch_deactivation_wait_for_the_same_cross_replica_acceptance_reservation()
    {
        var connection=Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services=new ServiceCollection();services.AddLogging();services.AddTestMarten(connection,databaseSchemaName:"partner_lifecycle_"+Guid.NewGuid().ToString("N"));
        using var provider=services.BuildServiceProvider();var store=provider.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",connection}}).Build();
        foreach(var companyCase in new[]{true,false})
        {
            var tenant=new Mock<ITenantContext>();tenant.SetupGet(t=>t.TenantId).Returns("lifecycle-"+Guid.NewGuid().ToString("N"));
            var company=Company.Create("1234567-8","Synthetic owner");var branch=Branch.Create(company.Id,"Apartment");
            using(var seed=store.LightweightSession(tenant.Object.TenantId)){seed.Store(company);seed.Store(branch);await seed.SaveChangesAsync();}
            using var acceptanceSession=new TenantSessionFactory(store,tenant.Object);using var deactivateSession=new TenantSessionFactory(store,tenant.Object);
            var acceptance=new ShiftWorkReservation(config,acceptanceSession);var signalled=new SignalledReservation(new ShiftWorkReservation(config,deactivateSession));
            var lease=await acceptance.AcquireAsync(default);
            Task operation=companyCase
                ? new CompanyHandlers.DeactivateCompanyCommandHandler(new CompanyRepository(deactivateSession),signalled,new ShiftRepository(deactivateSession)).Handle(new(company.Id),default)
                : new CompanyHandlers.BranchCommands.DeactivateBranchCommandHandler(new BranchRepository(deactivateSession),signalled).Handle(new(branch.Id),default);
            try
            {
                await signalled.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(operation.IsCompleted);
                using var read=store.QuerySession(tenant.Object.TenantId);
                Assert.True((await read.LoadAsync<Company>(company.Id))!.IsActive);
                Assert.True((await read.LoadAsync<Branch>(branch.Id))!.IsActive);
            }
            finally { await lease.DisposeAsync(); }
            await operation;
            using var check=store.QuerySession(tenant.Object.TenantId);
            if(companyCase)Assert.False((await check.LoadAsync<Company>(company.Id))!.IsActive);
            else Assert.False((await check.LoadAsync<Branch>(branch.Id))!.IsActive);
        }
    }
    private sealed class SignalledReservation(IShiftWorkReservation inner):IShiftWorkReservation
    {
        public TaskCompletionSource Requested=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct=default){Requested.TrySetResult();return await inner.AcquireAsync(ct);}
    }
}
