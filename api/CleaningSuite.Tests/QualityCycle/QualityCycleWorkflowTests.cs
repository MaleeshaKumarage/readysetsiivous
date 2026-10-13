using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Moq;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleWorkflowTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    [Fact]
    public async Task ExistingShift_TemplateAttachmentPreservesHistoryAndDispatchesWithoutReassignment()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Existing shift", Daily(), null, null, null);
        shift.CreatedUtc = Now.AddDays(-30);
        var template = QualityCycleTemplate.Create("Checklist", new List<string> { "Floor" });
        var shifts = new Mock<IShiftRepository>(); var forms = new Mock<IQualityCycleRepository>(); var dispatch = new Mock<IQualityCycleDispatcher>();
        shifts.Setup(x => x.GetAsync(shift.Id, default)).ReturnsAsync(shift);
        forms.Setup(x => x.GetTemplateAsync(template.Id, default)).ReturnsAsync(template);
        var handler = new SetShiftQualityCycleTemplateHandler(shifts.Object, forms.Object, dispatch.Object, new Clock());
        await handler.Handle(new(shift.Id, template.Id), default);
        Assert.Equal(template.Id, shift.QualityCycleTemplateId);
        Assert.Single(shift.QualityCyclePlans);
        Assert.Equal(Now, shift.QualityCyclePlans[0].EffectiveFromUtc);
        dispatch.Verify(x => x.EnsureUpcomingAsync(shift.Id, default), Times.Once);
        // A retry must recover generation without adding another plan.
        await handler.Handle(new(shift.Id, template.Id), default);
        Assert.Single(shift.QualityCyclePlans);
        dispatch.Verify(x => x.EnsureUpcomingAsync(shift.Id, default), Times.Exactly(2));
    }
    [Fact]
    public async Task TemplateFromDifferentBranchIsRejectedBeforeSaving()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift", Daily(), null, null, null);
        var template = QualityCycleTemplate.Create("Checklist", new List<string> { "Floor" }, branchId: Guid.NewGuid());
        var shifts = new Mock<IShiftRepository>(); var forms = new Mock<IQualityCycleRepository>(); var dispatch = new Mock<IQualityCycleDispatcher>();
        shifts.Setup(x => x.GetAsync(shift.Id, default)).ReturnsAsync(shift);
        forms.Setup(x => x.GetTemplateAsync(template.Id, default)).ReturnsAsync(template);
        await Assert.ThrowsAsync<ShiftConflictException>(() => new SetShiftQualityCycleTemplateHandler(shifts.Object, forms.Object, dispatch.Object, new Clock()).Handle(new(shift.Id, template.Id), default));
        shifts.Verify(x => x.SaveAsync(It.IsAny<Shift>(), default), Times.Never);
    }
    [Fact]
    public void CompletionRejectsMissingItemsAndAllowsUncheckedItemsWithoutNotes()
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Checklist", Now, new List<string> { "A", "B" });
        Assert.Throws<ArgumentException>(() => form.Submit(new List<QualityCycleFormItem> { new QualityCycleFormItem { ItemText = "A", IsChecked = true } }, null, "reason"));
        var items = new List<QualityCycleFormItem> { new QualityCycleFormItem { ItemText = "A", IsChecked = true }, new QualityCycleFormItem { ItemText = "B" } };
        Assert.False(form.IsSubmitted);
        form.Submit(items, null, null);
        Assert.Equal("Recorded", form.TaskStatus);
    }
    [Fact]
    public async Task MonthlyReportSeparatesWeekdayAndBiweeklyShiftsAndCountsHoursOncePerOccurrence()
    {
        var company = Company.Create("1234567-8", "AAA"); var branch = Branch.Create(company.Id, "AAA1");
        var weekday = new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = new() { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }, WeeklyStart = TimeSpan.FromHours(8), WeeklyEnd = TimeSpan.FromHours(10) };
        var biweekly = new ShiftSchedule { Type = ShiftScheduleType.BiWeekly, BiWeeklyDay = DayOfWeek.Monday, BiWeeklyWeekParity = 1, BiWeeklyStart = TimeSpan.FromHours(8), BiWeeklyEnd = TimeSpan.FromHours(11) };
        var a = Shift.Create(company.Id, branch.Id, "Weekdays", weekday, null, null, null, Guid.NewGuid());
        var b = Shift.Create(company.Id, branch.Id, "Biweekly", biweekly, null, null, null, Guid.NewGuid());
        a.CreatedUtc = b.CreatedUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var shifts = new Mock<IShiftRepository>(); var forms = new Mock<IQualityCycleRepository>();
        var companies = new Mock<ICompanyRepository>(); var branches = new Mock<IBranchRepository>();
        companies.Setup(x => x.GetAsync(company.Id, default)).ReturnsAsync(company); branches.Setup(x => x.GetAsync(branch.Id, default)).ReturnsAsync(branch);
        shifts.Setup(x => x.ListForReportingAsync(default)).ReturnsAsync(new[] { a, b });
        forms.Setup(x => x.ListFormsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), default)).ReturnsAsync(Array.Empty<QualityCycleForm>());
        foreach (var shift in new[] { a, b }) {
            var assignments = new[] { ShiftAssignment.Create(shift.Id, Guid.NewGuid(), null), ShiftAssignment.Create(shift.Id, Guid.NewGuid(), null) };
            foreach (var assignment in assignments) assignment.AssignedAtUtc = a.CreatedUtc;
            shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, default)).ReturnsAsync(assignments);
        }
        var report = await new QualityCycleMonthlyReportService(shifts.Object, forms.Object, companies.Object, branches.Object, new Mock<ITenantProfileRepository>().Object, new Mock<ITenantContext>().Object, new Clock()).BuildAsync(new(2026, 10), default);
        Assert.Equal(2, report.Shifts.Count);
        var weekdayRow = Assert.Single(report.Shifts.Where(x => x.ShiftId == a.Id));
        Assert.Equal(22, weekdayRow.ScheduledOccurrences); Assert.Equal(44m, weekdayRow.ScheduledHours); Assert.Equal(44, weekdayRow.MissingTasks);
        var biweeklyRow = Assert.Single(report.Shifts.Where(x => x.ShiftId == b.Id));
        Assert.InRange(biweeklyRow.ScheduledOccurrences, 2, 3); Assert.Equal(biweeklyRow.ScheduledOccurrences * 3m, biweeklyRow.ScheduledHours);
        Assert.All(report.Shifts, x => Assert.Equal(branch.Id, x.BranchId));
    }
    [Fact]
    public async Task ActivityCountsAreDistinctDaysAndSharedSnapshotContainsNoCleanerEvidence()
    {
        var company = Company.Create("1234567-8", "AAA"); var branch = Branch.Create(company.Id, "AAA1");
        var shift = Shift.Create(company.Id, branch.Id, "Weekdays", Daily(), null, null, null, Guid.NewGuid());
        shift.CreatedUtc = Now.AddMonths(-1);
        var shifts = new Mock<IShiftRepository>(); var forms = new Mock<IQualityCycleRepository>();
        var companies = new Mock<ICompanyRepository>(); var branches = new Mock<IBranchRepository>();
        companies.Setup(x => x.GetAsync(company.Id, default)).ReturnsAsync(company); branches.Setup(x => x.GetAsync(branch.Id, default)).ReturnsAsync(branch);
        shifts.Setup(x => x.GetAsync(shift.Id, default)).ReturnsAsync(shift);
        var records = new List<QualityCycleForm>();
        for (var day = 1; day <= 12; day++) for (var cleaner = 0; cleaner < 2; cleaner++) {
            var form = QualityCycleForm.Create(shift.Id, shift.Name, Guid.NewGuid(), "PRIVATE CLEANER", shift.QualityCycleTemplateId!.Value, "Activities",
                new DateTime(2026, 10, day, 8, 0, 0, DateTimeKind.Utc), new() { "Corridor", "Gym" }, new DateTime(2026, 10, day, 10, 0, 0, DateTimeKind.Utc));
            form.CompanyId = company.Id; form.BranchId = branch.Id;
            form.Submit(new() { new() { ItemText = "Corridor", IsChecked = true }, new() { ItemText = "Gym", IsChecked = day <= 4 } }, null, "PRIVATE NOTES");
            records.Add(form);
        }
        forms.Setup(x => x.ListFormsAsync(shift.Id, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), default)).ReturnsAsync(records);
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, default)).ReturnsAsync(Array.Empty<ShiftAssignment>());
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns("tenant-a");
        var service = new QualityCycleMonthlyReportService(shifts.Object, forms.Object, companies.Object, branches.Object, new Mock<ITenantProfileRepository>().Object, context.Object, new Clock());
        var report = await service.BuildAsync(new(2026, 10, ShiftId: shift.Id), default);
        var row = Assert.Single(report.Shifts);
        Assert.Equal(12, row.Items.Single(x => x.ItemText == "Corridor").DaysPerformed);
        Assert.Equal(12, row.Items.Single(x => x.ItemText == "Corridor").RecordedOccurrences);
        Assert.Equal(4, row.Items.Single(x => x.ItemText == "Gym").DaysPerformed);
        QualityCycleReportShare? saved = null;
        forms.Setup(x => x.SaveReportShareAsync(It.IsAny<QualityCycleReportShare>(), default)).Callback<QualityCycleReportShare, CancellationToken>((share, _) => saved = share).Returns(Task.CompletedTask);
        var link = await new CreateQualityCycleReportShareHandler(service, forms.Object, context.Object, new Clock()).Handle(new(2026, 10, ShiftId: shift.Id), default);
        Assert.Equal(Now.AddDays(30), link.ExpiresUtc);
        Assert.DoesNotContain("PRIVATE CLEANER", saved!.ReportJson); Assert.DoesNotContain("PRIVATE NOTES", saved.ReportJson);
        Assert.DoesNotContain(records[0].Token, saved.ReportJson);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<QualityCycleMonthlyReport>(saved.ReportJson)!;
        Assert.Empty(snapshot.Shifts[0].Forms); Assert.Equal(12, snapshot.Shifts[0].Items.Single(x => x.ItemText == "Corridor").DaysPerformed);
        var pdf = new CleaningSuite.Infrastructure.QualityCycle.QualityCyclePdfGenerator().GenerateMonthlyReportPdf(snapshot);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
    [Theory]
    [InlineData(true, 30)] [InlineData(false, 0)] [InlineData(false, -1)]
    public async Task RevokedOrExpiredSharedReportsCannotBeOpened(bool revoked, int daysRemaining)
    {
        var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(x => x.GetReportShareByTokenAsync("token", default)).ReturnsAsync(new QualityCycleReportShare { IsRevoked = revoked, ExpiresUtc = Now.AddDays(daysRemaining) });
        var pdf = new Mock<IQualityCyclePdfGenerator>();
        await Assert.ThrowsAsync<CleaningSuite.Application.Common.NotFoundException>(() => new GetSharedQualityCycleReportPdfHandler(repository.Object, pdf.Object, new Clock()).Handle(new("token"), default));
        pdf.Verify(x => x.GenerateMonthlyReportPdf(It.IsAny<QualityCycleMonthlyReport>()), Times.Never);
    }
    [Fact]
    public async Task RecurringGenerationIsIdempotentAndTemplateChangesPreserveStartedRecords()
    {
        var clock = new MutableClock { Value = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc) };
        var template = QualityCycleTemplate.Create("Activities", new() { "Corridor" });
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily", Daily(), null, null, null, template.Id);
        shift.CreatedUtc = clock.Value.AddDays(-1);
        var employee = new CleaningSuite.Domain.Employees.Employee { Id = Guid.NewGuid(), FirstName = "Cleaner", Email = "test@example.com", IsActive = true };
        var assignment = ShiftAssignment.Create(shift.Id, employee.Id, null); assignment.AssignedAtUtc = shift.CreatedUtc;
        var shifts = new Mock<IShiftRepository>(); var forms = new Mock<IQualityCycleRepository>();
        var employees = new Mock<CleaningSuite.Application.Employees.IEmployeeRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, default)).ReturnsAsync(shift);
        shifts.Setup(x => x.ListAssignmentsByShiftAsync(shift.Id, default)).ReturnsAsync(new[] { assignment });
        employees.Setup(x => x.GetByIdAsync(employee.Id, default)).ReturnsAsync(employee);
        forms.Setup(x => x.GetTemplateAsync(template.Id, default)).ReturnsAsync(template);
        var records = new Dictionary<DateTime, QualityCycleForm>();
        forms.Setup(x => x.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, It.IsAny<DateTime>(), default)).ReturnsAsync((Guid _, Guid _, DateTime time, CancellationToken _) => records.GetValueOrDefault(time));
        forms.Setup(x => x.TryCreateFormAsync(It.IsAny<QualityCycleForm>(), default)).ReturnsAsync((QualityCycleForm form, CancellationToken _) => records.TryAdd(form.ShiftOccurrenceUtc, form));
        var dispatcher = new QualityCycleDispatcher(shifts.Object, forms.Object, employees.Object, clock: clock);
        Assert.Equal(32, await dispatcher.EnsureUpcomingAsync(shift.Id));
        Assert.Equal(0, await dispatcher.EnsureUpcomingAsync(shift.Id));
        Assert.All(records.Values, form => { Assert.Null(form.EmailSentUtc); Assert.Null(form.EmailLastAttemptUtc); });
        var today = records[clock.Value.Date.AddHours(8)];
        today.EmailLastAttemptUtc = clock.Value.AddHours(-1); today.EmailError = "Historical delivery failure";
        Assert.Equal(0, await dispatcher.DispatchAsync(shift, new[] { new ShiftOccurrence { StartUtc = today.ShiftOccurrenceUtc, EndUtc = today.ShiftOccurrenceEndUtc } }));
        Assert.Equal("Historical delivery failure", today.EmailError); Assert.Equal(clock.Value.AddHours(-1), today.EmailLastAttemptUtc); Assert.Null(today.EmailSentUtc);
        var previouslyStarted = records[clock.Value.Date.AddDays(2).AddHours(8)]; previouslyStarted.Start();
        template.Items = new() { "Gym" };
        await dispatcher.EnsureUpcomingAsync(shift.Id);
        Assert.Equal("Corridor", records[clock.Value.Date.AddHours(8)].Items[0].ItemText);
        Assert.Equal("Gym", records[clock.Value.Date.AddDays(1).AddHours(8)].Items[0].ItemText);
        Assert.Equal("Corridor", previouslyStarted.Items[0].ItemText);
        clock.Value = clock.Value.AddDays(1);
        await dispatcher.EnsureUpcomingAsync(shift.Id);
        Assert.Equal(33, records.Count);
        Assert.All(records.Values, form => Assert.Null(form.EmailSentUtc));
    }
    private sealed class MutableClock : TimeProvider { public DateTime Value { get; set; } public override DateTimeOffset GetUtcNow() => Value; }
    private static ShiftSchedule Daily() => new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) };
}

public class QualityCycleDatabaseFactAttribute : FactAttribute
{
    public QualityCycleDatabaseFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING"))) Skip = "Set QC_TEST_CONNECTION_STRING to an isolated PostgreSQL database."; }
}
public class QualityCycleRepositoryIntegrationTests
{
    [QualityCycleDatabaseFact]
    public async Task TenantIsolationConcurrentGenerationAndOptimisticCompletion()
    {
        var options = new StoreOptions(); options.Connection(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!);
        options.Policies.AllDocumentsAreMultiTenanted(); options.Schema.For<QualityCycleForm>().UniqueIndex(x => x.Token);
        using var store = new DocumentStore(options);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var a = "qc-test-" + Guid.NewGuid().ToString("N"); var b = "qc-test-" + Guid.NewGuid().ToString("N");
        var contextA = new Mock<ITenantContext>(); contextA.SetupGet(x => x.TenantId).Returns(a);
        var contextB = new Mock<ITenantContext>(); contextB.SetupGet(x => x.TenantId).Returns(b);
        using var tenantA = new TenantSessionFactory(store, contextA.Object); using var tenantB = new TenantSessionFactory(store, contextB.Object);
        var repoA = new QualityCycleRepository(tenantA, store); var repoB = new QualityCycleRepository(tenantB, store);
        var template = QualityCycleTemplate.Create("Private", new List<string> { "Floor" }); await repoA.SaveTemplateAsync(template);
        Assert.Null(await repoB.GetTemplateAsync(template.Id)); Assert.Empty(await repoB.ListTemplatesAsync(null, null));
        var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", template.Id, template.Title, start, template.Items, start.AddHours(2));
        form.Id = QualityCycleDispatcher.TaskId(form.ShiftId, form.EmployeeId, start);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => repoA.TryCreateFormAsync(form)));
        Assert.Single(results.Where(x => x));
        Assert.Null(await repoB.GetFormByTokenAsync(form.Token));
        var share = new QualityCycleReportShare { ExpiresUtc = DateTime.UtcNow.AddDays(30), ReportJson = "{}" };
        await repoA.SaveReportShareAsync(share);
        Assert.Null(await repoB.GetReportShareByTokenAsync(share.Token));
        Assert.Empty(await repoB.ListReportSharesAsync());
        Assert.NotNull(await repoA.GetFormByShiftOccurrenceAsync(form.ShiftId, form.EmployeeId, start));
        Assert.Single(await repoA.ListFormsAsync(form.ShiftId, start, start.AddMonths(1)));
        using var staleTenant = new TenantSessionFactory(store, contextA.Object); var staleRepo = new QualityCycleRepository(staleTenant, store);
        var stale = (await staleRepo.GetFormByTokenAsync(form.Token))!;
        var loaded = (await repoA.GetFormByTokenAsync(form.Token))!;
        loaded.Submit(new List<QualityCycleFormItem> { new QualityCycleFormItem { ItemText = "Floor", IsChecked = true } }, null, null); await repoA.SaveFormAsync(loaded);
        stale.Submit(new List<QualityCycleFormItem> { new QualityCycleFormItem { ItemText = "Floor", IsChecked = false } }, null, "exception");
        await Assert.ThrowsAsync<JasperFx.ConcurrencyException>(() => staleRepo.SaveFormAsync(stale));
    }
}
