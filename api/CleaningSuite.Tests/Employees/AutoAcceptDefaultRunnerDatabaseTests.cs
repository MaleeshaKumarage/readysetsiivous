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
    public async Task Startup_switches_only_employees_created_before_cutoff_and_keeps_choices_made_after_it()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "auto_accept_default_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "auto-default-" + Guid.NewGuid().ToString("N");
        var cutoff = DateTime.UtcNow;

        // Existing and opted out before the release: switched.
        var existingOff = new Employee { Role = Employee.RoleEmployee, AutoAcceptWork = false, AutoAcceptWorkChosenUtc = cutoff.AddDays(-2), CreatedUtc = cutoff.AddDays(-30), Email = "off@example.test" };
        var existingAdmin = new Employee { Role = Employee.RoleAdmin, AutoAcceptWork = false, CreatedUtc = cutoff.AddDays(-30), Email = "admin@example.test" };
        // Existing, opted out after the release: their choice stays.
        var chosenLater = new Employee { Role = Employee.RoleEmployee, AutoAcceptWork = false, AutoAcceptWorkChosenUtc = cutoff.AddMinutes(5), CreatedUtc = cutoff.AddDays(-30), Email = "later@example.test" };
        // Created after the release: keeps the default, never switched by the one-time run.
        var created = new Employee { Role = Employee.RoleEmployee, AutoAcceptWork = false, CreatedUtc = cutoff.AddHours(1), Email = "new@example.test" };
        var partner = new Employee { Role = Employee.RolePartner, AutoAcceptWork = false, CreatedUtc = cutoff.AddDays(-30), Email = "partner@example.test" };
        using (var seed = store.LightweightSession(tenant))
        {
            seed.Store(existingOff, existingAdmin, chosenLater, created, partner);
            await seed.SaveChangesAsync();
        }

        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new TenantRegistration { Slug = tenant, Status = TenantRegistration.StatusActive }]);
        var runner = new AutoAcceptDefaultRunner(store, registry.Object, NullLogger<AutoAcceptDefaultRunner>.Instance);

        // Without a cutoff nothing is switched and no marker is written.
        Assert.Empty(await runner.RunAsync(null, default));
        using (var reader = store.QuerySession(tenant))
            Assert.False(await reader.Query<DataCorrectionRecord>().AnyAsync());

        var first = await runner.RunAsync(cutoff, default);
        Assert.Equal(2, first.Single(x => x.Tenant == tenant).Switched);
        using (var reader = store.QuerySession(tenant))
        {
            Assert.True((await reader.LoadAsync<Employee>(existingOff.Id))!.AutoAcceptWork);
            Assert.True((await reader.LoadAsync<Employee>(existingAdmin.Id))!.AutoAcceptWork);
            Assert.False((await reader.LoadAsync<Employee>(chosenLater.Id))!.AutoAcceptWork);
            Assert.False((await reader.LoadAsync<Employee>(created.Id))!.AutoAcceptWork);
            Assert.False((await reader.LoadAsync<Employee>(partner.Id))!.AutoAcceptWork);
            Assert.True(await reader.Query<DataCorrectionRecord>().AnyAsync());
        }

        // A later start finds the marker and changes nothing, even for an employee who switches off afterwards.
        using (var writer = store.LightweightSession(tenant))
        {
            var employee = await writer.LoadAsync<Employee>(existingOff.Id);
            employee!.AutoAcceptWork = false; employee.AutoAcceptWorkChosenUtc = DateTime.UtcNow;
            writer.Store(employee);
            await writer.SaveChangesAsync();
        }
        var second = await runner.RunAsync(cutoff, default);
        Assert.DoesNotContain(second, x => x.Tenant == tenant);
        using (var reader = store.QuerySession(tenant))
            Assert.False((await reader.LoadAsync<Employee>(existingOff.Id))!.AutoAcceptWork);
    }
}
