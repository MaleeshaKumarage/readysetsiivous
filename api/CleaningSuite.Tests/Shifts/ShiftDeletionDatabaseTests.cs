using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text.Json;

namespace CleaningSuite.Tests.Shifts;

public class ShiftDeletionDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Deletion_unlinks_roster_preserves_QC_evidence_reports_and_other_tenants()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "deleted_shifts_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "fixture-" + Guid.NewGuid().ToString("N");
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(tenant);
        var day = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
        var company = new Company { Id = Guid.NewGuid(), Name = "Company", IsActive = true };
        var branch = new Branch { Id = Guid.NewGuid(), CompanyId = company.Id, Name = "Branch", IsActive = true };
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, BranchId = branch.Id, Name = "Corridor", IsActive = false,
            Schedule = new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) } };
        var employee = Guid.NewGuid();
        var assignment = new ShiftAssignment { ShiftId = shift.Id, EmployeeId = employee, IsActive = true, AssignedAtUtc = day.AddDays(-2) };
        var form = QualityCycleForm.Create(shift.Id, shift.Name, employee, "Cleaner", Guid.NewGuid(), "Activities", day, ["Corridor"], day.AddHours(2));
        form.CompanyId = company.Id; form.BranchId = branch.Id;
        form.Start(day); form.Submit([new() { ItemText = "Corridor", IsChecked = true }], null, "Completed", [new("data:image/png;base64,fixture", "Floor cleaned")], day.AddHours(2));
        var exception = ShiftOccurrenceException.Create(shift.Id, null, day, day.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Extra", employee);
        using (var writer = store.LightweightSession(tenant)) { writer.Store(shift); writer.Store(assignment); writer.Store(form); writer.Store(exception); await writer.SaveChangesAsync(); }
        using (var writer = store.LightweightSession(tenant + "-foreign"))
        {
            var foreignShift = JsonSerializer.Deserialize<Shift>(JsonSerializer.Serialize(shift))!; foreignShift.Version = Guid.Empty;
            var foreignForm = JsonSerializer.Deserialize<QualityCycleForm>(JsonSerializer.Serialize(form))!; foreignForm.Version = Guid.Empty; foreignForm.Token = Guid.NewGuid().ToString("N");
            writer.Store(foreignShift); writer.Store(foreignForm); await writer.SaveChangesAsync();
        }
        using var scoped = new TenantSessionFactory(store, context.Object);
        var shifts = new ShiftRepository(scoped);
        var forms = new QualityCycleRepository(scoped, store);
        var companies = new Mock<ICompanyRepository>(); companies.Setup(x => x.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        var branches = new Mock<IBranchRepository>(); branches.Setup(x => x.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        var profiles = new Mock<ITenantProfileRepository>(); profiles.Setup(x => x.GetAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "UTC" });
        var reports = new QualityCycleMonthlyReportService(shifts, forms, companies.Object, branches.Object, profiles.Object, context.Object, TimeProvider.System);
        var before = await reports.BuildAsync(new(2026, 10, ShiftId: shift.Id), default);
        await shifts.DeleteAsync(shift.Id);
        Assert.Null(await shifts.GetAsync(shift.Id));
        Assert.Empty(await shifts.ListAssignmentsByEmployeeAsync(employee));
        Assert.Empty(await shifts.ListOccurrenceExceptionsByEmployeeAsync(employee, day.AddDays(-1), day.AddDays(1)));
        var retained = await forms.GetFormAsync(form.Id);
        Assert.NotNull(retained!.ShiftDeletedUtc);
        Assert.True(retained.IsSubmitted); Assert.Equal("Floor cleaned", Assert.Single(retained.Photos).Note);
        Assert.NotNull(await shifts.GetDeletedSnapshotAsync(shift.Id));
        var after = await reports.BuildAsync(new(2026, 10, ShiftId: shift.Id), default);
        Assert.Equal(JsonSerializer.Serialize(before.Shifts), JsonSerializer.Serialize(after.Shifts));
        using var foreign = store.QuerySession(tenant + "-foreign");
        Assert.NotNull(await foreign.LoadAsync<Shift>(shift.Id));
        Assert.Null((await foreign.LoadAsync<QualityCycleForm>(form.Id))!.ShiftDeletedUtc);
    }
}
