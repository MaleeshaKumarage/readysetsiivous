using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CleaningSuite.Tests.Employees;

public class AutoAcceptDefaultRunnerDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Startup_switches_work_employees_once_and_keeps_later_choices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "auto_accept_default_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "auto-default-" + Guid.NewGuid().ToString("N");

        var offEmployee = new Employee { Role = Employee.RoleEmployee, AutoAcceptWork = false, AutoAcceptWorkChosenUtc = DateTime.UtcNow, Email = "off@example.test" };
        var offAdmin = new Employee { Role = Employee.RoleAdmin, AutoAcceptWork = false, Email = "admin@example.test" };
        var partner = new Employee { Role = Employee.RolePartner, AutoAcceptWork = false, Email = "partner@example.test" };
        using (var seed = store.LightweightSession(tenant))
        {
            seed.Store(offEmployee, offAdmin, partner);
            await seed.SaveChangesAsync();
        }

        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new TenantRegistration { Slug = tenant, Status = TenantRegistration.StatusActive }]);
        var runner = new AutoAcceptDefaultRunner(store, registry.Object, NullLogger<AutoAcceptDefaultRunner>.Instance);

        var first = await runner.RunAsync(default);
        Assert.Equal(2, first.Single(x => x.Tenant == tenant).Switched);
        using (var reader = store.QuerySession(tenant))
        {
            Assert.True((await reader.LoadAsync<Employee>(offEmployee.Id))!.AutoAcceptWork);
            Assert.True((await reader.LoadAsync<Employee>(offAdmin.Id))!.AutoAcceptWork);
            Assert.False((await reader.LoadAsync<Employee>(partner.Id))!.AutoAcceptWork);
            Assert.True(await reader.Query<DataCorrectionRecord>().AnyAsync());
        }

        // An employee who switches off after the one-time run keeps that choice on the next start.
        using (var writer = store.LightweightSession(tenant))
        {
            var employee = await writer.LoadAsync<Employee>(offEmployee.Id);
            employee!.AutoAcceptWork = false; employee.AutoAcceptWorkChosenUtc = DateTime.UtcNow;
            writer.Store(employee);
            await writer.SaveChangesAsync();
        }
        var second = await runner.RunAsync(default);
        Assert.DoesNotContain(second, x => x.Tenant == tenant);
        using (var reader = store.QuerySession(tenant))
            Assert.False((await reader.LoadAsync<Employee>(offEmployee.Id))!.AutoAcceptWork);
    }
}
