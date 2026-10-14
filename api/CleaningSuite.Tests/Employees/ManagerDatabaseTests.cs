using Marten.Newtonsoft;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using MediatR;
using Moq;

namespace CleaningSuite.Tests.Employees;
public class ManagerDatabaseTests
{
    [EmployeeIdentityDatabaseFact]
    public async Task RealManagerEditCannotCrossTenantsOrOverwriteConcurrentAdminTemplateChange()
    {
        var options = new StoreOptions(); options.RestoreV8Defaults(); options.UseNewtonsoftForSerialization();
        options.DatabaseSchemaName = "fixture_" + Guid.NewGuid().ToString("N"); options.Connection(Environment.GetEnvironmentVariable("INVITE_TEST_DATABASE")!);
        options.Policies.AllDocumentsAreMultiTenanted(); options.Policies.ForAllDocuments(d => d.UseOptimisticConcurrency = true);
        options.Schema.For<Company>(); options.Schema.For<Branch>(); options.Schema.For<Shift>();
        using var store = new DocumentStore(options); await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "manager-db-" + Guid.NewGuid().ToString("N");
        var company = Company.Create("MANAGER-TEST-" + Guid.NewGuid().ToString("N"), "Company"); var branch = Branch.Create(company.Id, "Branch");
        var shift = Shift.Create(company.Id, branch.Id, "Original", new() { Type = ShiftScheduleType.DailySameTime,
            DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, "Original note", null, null, Guid.NewGuid());
        var foreignCompany = Company.Create("FOREIGN-TEST-" + Guid.NewGuid().ToString("N"), "Foreign company");
        var foreignBranch = Branch.Create(foreignCompany.Id, "Foreign branch");
        var foreign = Shift.Create(foreignCompany.Id, foreignBranch.Id, "Foreign tenant shift", shift.Schedule, null, null, null);
        using (var session = store.LightweightSession(tenant)) { session.Store(company); session.Store(branch); session.Store(shift); await session.SaveChangesAsync(); }
        using (var session = store.LightweightSession(tenant + "-other")) { session.Store(foreignCompany); session.Store(foreignBranch); session.Store(foreign); await session.SaveChangesAsync(); }
        var context = new Mock<ITenantContext>(); context.SetupGet(t => t.TenantId).Returns(tenant);
        using var managerSession = new TenantSessionFactory(store, context.Object); using var adminSession = new TenantSessionFactory(store, context.Object);
        var managerRepository = new ShiftRepository(managerSession); var adminRepository = new ShiftRepository(adminSession);
        var dispatcher = new Mock<IQualityCycleDispatcher>();
        var operations = new ManagerOperations(managerRepository, Mock.Of<IEmployeeRepository>(), new CompanyRepository(managerSession),
            new BranchRepository(managerSession), dispatcher.Object, Mock.Of<IMediator>());
        await Assert.ThrowsAsync<NotFoundException>(() => operations.Get(foreign.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => operations.Update(foreign.Id, "Forged", foreign.Schedule, true, null, null, default));
        _ = await managerRepository.GetAsync(shift.Id); // A manager request has read this old version.
        var adminCopy = (await adminRepository.GetAsync(shift.Id))!;
        var adminTemplate = Guid.NewGuid(); adminCopy.QualityCycleTemplateId = adminTemplate; adminCopy.Notes = "Concurrent admin note";
        await adminRepository.SaveAsync(adminCopy);
        await Assert.ThrowsAsync<JasperFx.ConcurrencyException>(() => operations.Update(shift.Id, "Manager stale edit", shift.Schedule, true, null, null, default));
        using var query = store.QuerySession(tenant); var saved = (await query.LoadAsync<Shift>(shift.Id))!;
        Assert.Equal(adminTemplate, saved.QualityCycleTemplateId); Assert.Equal("Concurrent admin note", saved.Notes); Assert.Equal("Original", saved.Name);
        Assert.Empty(dispatcher.Invocations);
        adminCopy.Deactivate(); await adminRepository.SaveAsync(adminCopy);
        using var restoringSession = new TenantSessionFactory(store, context.Object); var restoringRepository = new ShiftRepository(restoringSession);
        _ = await restoringRepository.GetAsync(shift.Id);
        var winningTemplate = Guid.NewGuid(); adminCopy.QualityCycleTemplateId = winningTemplate; await adminRepository.SaveAsync(adminCopy);
        var restore = new CleaningSuite.Application.Shifts.ShiftHandlers.ReactivateShiftCommandHandler(restoringRepository,
            new CompanyRepository(restoringSession), new BranchRepository(restoringSession), dispatcher.Object);
        await Assert.ThrowsAsync<JasperFx.ConcurrencyException>(() => restore.Handle(new(shift.Id), default));
        using var finalQuery = store.QuerySession(tenant); var final = (await finalQuery.LoadAsync<Shift>(shift.Id))!;
        Assert.False(final.IsActive); Assert.Equal(winningTemplate, final.QualityCycleTemplateId); Assert.Equal("Concurrent admin note", final.Notes);
        Assert.Empty(dispatcher.Invocations);
        using var foreignQuery = store.QuerySession(tenant + "-other"); Assert.Equal("Foreign tenant shift", (await foreignQuery.LoadAsync<Shift>(foreign.Id))!.Name);
    }
}
