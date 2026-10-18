using System.Text.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.QualityCycle;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleSelectedReportTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Contract_start_before_data_entry_counts_past_scheduled_visits_without_rewriting_evidence(bool history)
    {
        var f = new Fixture();
        f.Shift.CreatedUtc = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        f.Shift.ValidFrom = new(2026, 10, 1);
        if (history) f.Shift.RecordQualityCyclePlan(f.Shift.CreatedUtc);
        var report = await f.Reports.BuildAsync(new([f.Shift.Id], ["2026-10"]), default);
        var row = Assert.Single(report.Shifts);
        Assert.Equal(31, row.ScheduledOccurrences); Assert.Equal(62, row.ScheduledHours);
        Assert.Empty(f.Records);
        if (history) Assert.Equal(f.Shift.CreatedUtc, f.Shift.QualityCyclePlans[0].EffectiveFromUtc);
        else Assert.Empty(f.Shift.QualityCyclePlans);
    }
    private static readonly DateTime Now = new(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    [Fact]
    public void Period_validation_rejects_ambiguous_invalid_or_excessive_requests_and_deduplicates_months()
    {
        var ids = new[] { Guid.NewGuid() };
        Assert.Throws<ValidationException>(() => new QualityCycleReportSelection([], ["2026-10"]).ValidatePeriods());
        Assert.Throws<ValidationException>(() => new QualityCycleReportSelection(ids, ["2026-13"]).ValidatePeriods());
        Assert.Throws<ValidationException>(() => new QualityCycleReportSelection(ids, ["2026-10"], new(2026, 10, 1), new(2026, 10, 2)).ValidatePeriods());
        Assert.Throws<ValidationException>(() => new QualityCycleReportSelection(ids, From: new(2026, 10, 2), To: new(2026, 10, 1)).ValidatePeriods());
        Assert.Throws<ValidationException>(() => new QualityCycleReportSelection(ids, From: new(2025, 1, 1), To: new(2026, 1, 2)).ValidatePeriods());
        Assert.Throws<ValidationException>(() => new QualityCycleReportSelection(ids, ["2026-10"], Language: "de").ValidatePeriods());
        var periods = new QualityCycleReportSelection(ids, ["2026-12", "2026-10", "2026-10"]).ValidatePeriods();
        Assert.Equal(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 1) }, periods.Select(p => p.From));
    }

    private sealed class Fixture
    {
        public Mock<IShiftRepository> Shifts { get; } = new();
        public Mock<IQualityCycleRepository> Forms { get; } = new();
        public Mock<ICompanyRepository> Companies { get; } = new();
        public Mock<IBranchRepository> Branches { get; } = new();
        public Mock<ITenantContext> Tenant { get; } = new();
        public Shift Shift { get; }
        public List<QualityCycleForm> Records { get; } = new();
        public QualityCycleSelectedReportService Reports { get; }
        public Fixture()
        {
            var company = Company.Create("123", "AAA", contactEmail: "company@example.test");
            var branch = Branch.Create(company.Id, "AAA1"); branch.ContactEmail = "branch@example.test";
            Shift = Shift.Create(company.Id, branch.Id, "Daily", new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromMinutes(30), DailyEnd = TimeSpan.FromMinutes(150) }, null, null, null);
            Shift.CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Tenant.SetupGet(t => t.TenantId).Returns("tenant-a");
            Companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
            Branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
            Shifts.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Contains(Shift.Id) ? new[] { Shift } : Array.Empty<Shift>());
            var employee = Guid.NewGuid();
            Shifts.Setup(r => r.ListAssignmentHistoryAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
                new ShiftAssignment { EmployeeId = employee, ShiftId = Shift.Id, IsActive = true, AssignedAtUtc = Shift.CreatedUtc } });
            Forms.Setup(r => r.ListFormsAsync(Shift.Id, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid? _, DateTime? from, DateTime? to, CancellationToken _) => Records.Where(f => f.ShiftOccurrenceUtc >= from && f.ShiftOccurrenceUtc < to).ToArray());
            var profile = new Mock<ITenantProfileRepository>(); profile.Setup(r => r.GetAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
            Reports = new(new(Shifts.Object, Forms.Object, Companies.Object, Branches.Object, profile.Object, Tenant.Object, new Clock()));
        }
        public QualityCycleForm AddRecord(DateTime scheduled, bool completeTiming = true)
        {
            var form = QualityCycleForm.Create(Shift.Id, Shift.Name, Guid.NewGuid(), "PRIVATE CLEANER", Guid.NewGuid(), "Activities", scheduled, ["Corridor"], scheduled.AddHours(2));
            form.CompanyId = Shift.CompanyId; form.BranchId = Shift.BranchId;
            if (completeTiming) form.Start(scheduled.AddMinutes(10));
            form.Submit([new() { ItemText = "Corridor", IsChecked = true }], null, "Original note", endedAtUtc: scheduled.AddHours(2));
            Records.Add(form); return form;
        }
    }

    [Fact]
    public async Task Selected_months_aggregate_without_gap_month_or_duplicate_counts_including_shifts_without_checklists()
    {
        var f = new Fixture();
        f.AddRecord(new(2026, 9, 30, 21, 30, 0, DateTimeKind.Utc)); // Oct 1 Helsinki
        f.AddRecord(new(2026, 11, 30, 22, 30, 0, DateTimeKind.Utc)); // Dec 1 Helsinki
        f.AddRecord(new(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc)); // November excluded
        var report = await f.Reports.BuildAsync(new([f.Shift.Id, f.Shift.Id], ["2026-12", "2026-10", "2026-10"]), default);
        var row = Assert.Single(report.Shifts);
        Assert.Equal(62, row.ScheduledOccurrences); Assert.Equal(124m, row.ScheduledHours);
        Assert.Equal(2, row.SubmittedTasks); Assert.Equal(2, row.Forms.Count);
        Assert.Equal(3.6666666666666666666666666666m, row.RecordedStaffHours!.Value, precision: 6);
        Assert.Equal(2, report.Periods!.Count); Assert.Equal(2, row.Items.Single().DaysPerformed);
        f.Shifts.Verify(s => s.ListForReportingAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Inclusive_single_day_range_uses_tenant_midnight_and_preserves_unavailable_timing()
    {
        var f = new Fixture();
        f.AddRecord(new(2026, 9, 30, 21, 30, 0, DateTimeKind.Utc), completeTiming: false);
        f.AddRecord(new(2026, 10, 1, 21, 30, 0, DateTimeKind.Utc));
        var report = await f.Reports.BuildAsync(new([f.Shift.Id], From: new(2026, 10, 1), To: new(2026, 10, 1)), default);
        var row = Assert.Single(report.Shifts);
        Assert.Equal(1, row.ScheduledOccurrences); Assert.Single(row.Forms);
        Assert.Null(row.RecordedStaffHours); Assert.Equal(1, row.IncompleteTimingRecords);
        f.Forms.Verify(r => r.ListFormsAsync(f.Shift.Id, new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 21, 0, 0, DateTimeKind.Utc), default), Times.Once);
    }

    [Fact]
    public async Task Missing_or_foreign_selected_shift_is_rejected()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<QualityCycleConflictException>(() => f.Reports.BuildAsync(new([Guid.NewGuid()], ["2026-10"]), default));
    }

    [Fact]
    public async Task Selected_shift_without_visits_still_has_a_zero_summary_row()
    {
        var f = new Fixture(); f.Shift.ValidFrom = new DateTime(2027, 1, 1);
        var report = await f.Reports.BuildAsync(new([f.Shift.Id], ["2026-10"]), default);
        var row = Assert.Single(report.Shifts);
        Assert.Equal(0, row.ScheduledOccurrences); Assert.Equal(0, row.CompletedOccurrences);
        Assert.Equal(0m, row.ScheduledHours); Assert.Null(row.RecordedStaffHours);
    }

    [Fact]
    public async Task Recipient_options_use_company_and_branch_contacts_and_deduplicate_addresses()
    {
        var f = new Fixture();
        var handler = new RecipientOptionsHandler(f.Shifts.Object, f.Companies.Object, f.Branches.Object);
        var options = await handler.Handle(new([f.Shift.Id]), default);
        Assert.Equal(new[] { "company@example.test", "branch@example.test" }, options.Select(o => o.Email));
        var branch = (await f.Branches.Object.GetAsync(f.Shift.BranchId))!;
        branch.ContactEmail = "COMPANY@example.test";
        Assert.Single(await handler.Handle(new([f.Shift.Id, f.Shift.Id]), default));
    }

    [Fact]
    public async Task Branch_contact_email_is_validated_and_returned_after_create_and_edit()
    {
        var company = Company.Create("123", "AAA");
        var companies = new Mock<ICompanyRepository>(); companies.Setup(r => r.GetAsync(company.Id, default)).ReturnsAsync(company);
        var branches = new Mock<IBranchRepository>(); Branch? saved = null;
        branches.Setup(r => r.SaveAsync(It.IsAny<Branch>(), default)).Callback<Branch, CancellationToken>((b, _) => saved = b).Returns(Task.CompletedTask);
        var command = new CreateBranchCommand(company.Id, "Office", ContactEmail: "branch@example.test");
        Assert.True((await new CompanyValidators.CreateBranchCommandValidator().ValidateAsync(command)).IsValid);
        Assert.False((await new CompanyValidators.CreateBranchCommandValidator().ValidateAsync(command with { ContactEmail = "bad address" })).IsValid);
        var created = await new CompanyHandlers.BranchCommands.CreateBranchCommandHandler(branches.Object, companies.Object).Handle(command, default);
        Assert.Equal("branch@example.test", created.ContactEmail);
        branches.Setup(r => r.GetAsync(created.Id, default)).ReturnsAsync(() => saved);
        var edited = await new CompanyHandlers.BranchCommands.UpdateBranchCommandHandler(branches.Object).Handle(new(created.Id, "Office", ContactEmail: "new@example.test"), default);
        Assert.Equal("new@example.test", edited.ContactEmail); Assert.Equal("new@example.test", saved!.ContactEmail);
    }

    [Fact]
    public async Task Email_snapshot_is_safe_send_failure_is_visible_and_retry_preserves_snapshot_and_expiry()
    {
        var f = new Fixture(); f.AddRecord(new(2026, 9, 30, 21, 30, 0, DateTimeKind.Utc));
        QualityCycleReportShare? saved = null;
        f.Forms.Setup(r => r.SaveReportShareAsync(It.IsAny<QualityCycleReportShare>(), default)).Callback<QualityCycleReportShare, CancellationToken>((s, _) => saved = s).Returns(Task.CompletedTask);
        var sender = new Mock<IEmailSender>(); sender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default)).ThrowsAsync(new InvalidOperationException("PRIVATE PROVIDER DETAIL"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:FrontendBaseUrl"] = "https://site.test", ["Resend:ApiKey"] = "synthetic" }).Build();
        var delivery = new QualityCycleReportDelivery(f.Forms.Object, sender.Object, config, f.Tenant.Object, new Clock());
        var result = await new SendQualityCycleReportHandler(f.Reports, f.Forms.Object, delivery, new Clock()).Handle(new(new([f.Shift.Id], ["2026-10"], Language: "fi"), "reader@example.test"), default);
        Assert.NotNull(result.EmailError); Assert.DoesNotContain("PRIVATE", result.EmailError);
        Assert.NotNull(saved); var original = saved.ReportJson; var expiry = saved.ExpiresUtc;
        Assert.DoesNotContain("PRIVATE CLEANER", original); Assert.Empty(JsonSerializer.Deserialize<QualityCycleMonthlyReport>(original)!.Shifts.Single().Forms);
        Assert.DoesNotContain("reader@example.test", JsonSerializer.Serialize(ReportShareMapping.Public(saved)));
        sender.Setup(s => s.SendAsync("reader@example.test", "Siivouksen laaturaportti", It.Is<string>(html => html.Contains("/quality-report/?tenant=tenant-a") && html.Contains("Avaa raportti")), default)).Returns(Task.CompletedTask);
        f.Forms.Setup(r => r.GetReportShareAsync(saved.Id, default)).ReturnsAsync(saved);
        var retry = await new RetryQualityCycleReportEmailHandler(f.Forms.Object, delivery).Handle(new(saved.Id), default);
        Assert.Null(retry.EmailError); Assert.Equal(Now, retry.EmailSentUtc); Assert.Equal(original, saved.ReportJson); Assert.Equal(expiry, saved.ExpiresUtc);
        await delivery.SendAsync(saved, default);
        sender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Exactly(2));
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public async Task Expired_and_revoked_links_cannot_be_viewed_or_acknowledged(bool revoked, bool expired)
    {
        var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(r => r.GetReportShareByTokenAsync("token", default)).ReturnsAsync(new QualityCycleReportShare { IsRevoked = revoked, ExpiresUtc = expired ? Now : Now.AddDays(1) });
        var handler = new PublicReportHandler(repository.Object, new Clock());
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetSharedQualityCycleReportQuery("token"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new AcknowledgeQualityCycleReportCommand("token"), default));
        repository.Verify(r => r.AcknowledgeReportShareAsync(It.IsAny<string>(), It.IsAny<DateTime>(), default), Times.Never);
    }

    [Theory]
    [InlineData("en", "Cleaning Quality Report", "Photos & Notes", "Page")]
    [InlineData("fi", "Siivouksen laaturaportti", "Kuvat ja muistiinpanot", "Sivu")]
    [InlineData("sv", "Kvalitetsrapport för städning", "Foton och anteckningar", "Sida")]
    public async Task Localized_pdf_preserves_customer_notes_and_exact_selected_periods(string language, string title, string appendix, string pageLabel)
    {
        var f = new Fixture(); var sample = f.AddRecord(new(2026, 9, 30, 21, 30, 0, DateTimeKind.Utc));
        using (var bitmap = new SkiaSharp.SKBitmap(640, 360)) {
            using var canvas = new SkiaSharp.SKCanvas(bitmap); canvas.Clear(SkiaSharp.SKColors.LightSteelBlue);
            using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.DarkSlateBlue };
            canvas.DrawRect(40, 40, 560, 280, paint);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            sample.Photos.Add(new("data:image/png;base64," + Convert.ToBase64String(data.ToArray()), "Original photo note – käytävä cleaned"));
        }
        var report = await f.Reports.BuildAsync(new([f.Shift.Id], ["2026-10", "2026-12"], Language: language), default);
        var bytes = new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(report);
        Assert.Equal(title, ReportText.Get(language, "Cleaning Quality Report"));
        Assert.Equal(appendix, ReportText.Get(language, "Photos & Notes"));
        Assert.Equal(pageLabel, ReportText.Get(language, "Page"));
        Assert.True(PDFtoImage.Conversion.GetPageCount(bytes) >= 3);
        var output = Environment.GetEnvironmentVariable("QC_LOCALIZED_PDF_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) { Directory.CreateDirectory(output); await File.WriteAllBytesAsync(Path.Combine(output, $"quality-report-{language}.pdf"), bytes); }
    }
}
