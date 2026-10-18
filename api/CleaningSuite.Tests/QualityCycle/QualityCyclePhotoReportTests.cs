using System.Text.Json;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.QualityCycle;
using FluentValidation;
using Moq;
using PDFtoImage;
using SkiaSharp;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CleaningSuite.Api;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCyclePhotoReportTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 23, 30, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero); }
    private static QualityCycleForm Form() => QualityCycleForm.Create(Guid.NewGuid(), "Weekday shift", Guid.NewGuid(),
        "PRIVATE CLEANER", Guid.NewGuid(), "Activities", Start, new() { "Corridor" }, Start.AddHours(2));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \r\n\t ")]
    public void Photos_require_a_note_before_any_domain_state_changes(string? note)
    {
        var form = Form(); var updated = form.UpdatedUtc;
        var exception = Assert.Throws<ArgumentException>(() => form.Submit(new() { new() { ItemText = "Corridor", IsChecked = true } }, null, "General note is insufficient", new() { new("photo", note!) }));
        Assert.Equal("photos", exception.ParamName);
        Assert.False(form.IsSubmitted); Assert.False(form.Items[0].IsChecked);
        Assert.Null(form.EndedAtUtc); Assert.Null(form.SubmittedUtc); Assert.Equal(updated, form.UpdatedUtc);
        Assert.Empty(form.PhotoUrls); Assert.Null(form.CleanerNotes);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Authenticated_and_token_handlers_reject_photo_without_note_without_saving(bool authenticated)
    {
        var form = Form(); form.Start();
        var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(x => x.GetFormAsync(form.Id, default)).ReturnsAsync(form);
        repository.Setup(x => x.GetFormByTokenAsync(form.Token, default)).ReturnsAsync(form);
        var items = new List<QualityCycleFormItemDto> { new("Corridor", true) };
        var exception = authenticated
            ? await Assert.ThrowsAsync<ValidationException>(() => new QualityCycleHandlers.EndQualityCycleFormCommandHandler(repository.Object, NoopUploadContentValidator.Instance, new Clock()).Handle(new(form.Id, form.EmployeeId, items, new() { "photo" }, "  "), default))
            : await Assert.ThrowsAsync<ValidationException>(() => new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(repository.Object, NoopUploadContentValidator.Instance, new Clock()).Handle(new(form.Token, items, new() { "photo" }, null), default));
        Assert.Equal("Photos", Assert.Single(exception.Errors).PropertyName);
        Assert.False(form.IsSubmitted); Assert.False(form.Items[0].IsChecked);
        repository.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), default), Times.Never);
    }

    [Theory]
    [InlineData(null)] [InlineData("Standalone note")]
    public void No_photos_allow_optional_notes_and_unchecked_activities(string? note)
    {
        var form = Form(); form.Submit(new() { new() { ItemText = "Corridor" } }, new List<string>(), note);
        Assert.True(form.IsSubmitted); Assert.Empty(form.PhotoUrls); Assert.Equal(note, form.CleanerNotes);
    }

    [Fact]
    public async Task Http_submission_returns_note_validation_400_and_allows_note_only_retry()
    {
        var form = Form(); form.ShiftOccurrenceUtc = Start.Date.AddHours(8); form.ShiftOccurrenceEndUtc = Start.Date.AddHours(10); var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(x => x.GetFormByTokenAsync(form.Token, It.IsAny<CancellationToken>())).ReturnsAsync(form);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.GetAsync(form.ShiftId, It.IsAny<CancellationToken>())).ReturnsAsync(new Shift {
            Id = form.ShiftId, IsActive = true, Schedule = new() { Type = ShiftScheduleType.DailySameTime,
                DailyStart = form.ShiftOccurrenceUtc.TimeOfDay, DailyEnd = form.ShiftOccurrenceEndUtc.TimeOfDay } });
        shifts.Setup(x => x.ListAssignmentHistoryAsync(form.ShiftId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
            new ShiftAssignment { ShiftId = form.ShiftId, EmployeeId = form.EmployeeId, AssignedAtUtc = form.ShiftOccurrenceUtc.AddDays(-1), IsActive = true } });
        var employees = new Mock<CleaningSuite.Application.Employees.IEmployeeRepository>();
        employees.Setup(x => x.GetByIdAsync(form.EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(new CleaningSuite.Domain.Employees.Employee { Id = form.EmployeeId, IsActive = true });
        var profiles = new Mock<ITenantProfileRepository>(); profiles.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "UTC" });
        var reservation = new Mock<IShiftWorkReservation>(); reservation.Setup(x => x.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.test" }));
            builder.ConfigureTestServices(services => {
                foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<IQualityCycleRepository>(); services.AddSingleton(repository.Object);
                services.RemoveAll<IShiftRepository>(); services.AddSingleton(shifts.Object);
                services.RemoveAll<CleaningSuite.Application.Employees.IEmployeeRepository>(); services.AddSingleton(employees.Object);
                services.RemoveAll<ITenantProfileRepository>(); services.AddSingleton(profiles.Object);
                services.RemoveAll<IShiftWorkReservation>(); services.AddSingleton(reservation.Object);
                services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new Clock());
            });
        });
        using var client = factory.CreateClient();
        var route = $"/api/v1/public/tenant-a/quality-cycle/{form.Token}/submit";
        var items = new[] { new QualityCycleFormItemDto("Corridor", true) };
        var failed = await client.PostAsJsonAsync(route, new { token = form.Token, items, photos = new[] { new QualityCyclePhoto("photo", "  ") }, cleanerNotes = "General note" });
        Assert.True(failed.StatusCode == HttpStatusCode.BadRequest, await failed.Content.ReadAsStringAsync());
        Assert.Contains("Add a separate note for each photo", await failed.Content.ReadAsStringAsync());
        Assert.False(form.IsSubmitted); Assert.False(form.Items[0].IsChecked);
        repository.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
        var success = await client.PostAsJsonAsync(route, new { token = form.Token, items, photoUrls = Array.Empty<string>(), cleanerNotes = "Note without image" });
        Assert.Equal(HttpStatusCode.OK, success.StatusCode); Assert.True(form.IsSubmitted); Assert.Equal("Note without image", form.CleanerNotes);
    }

    [Fact]
    public async Task Report_projects_submitted_evidence_and_public_snapshot_retains_evidence_but_redacts_forms()
    {
        var company = Company.Create("7654321-0", "AAA"); var branch = Branch.Create(company.Id, "AAA1", "Siivouskatu 12", "00100", "Helsinki", "Finland");
        var shift = Shift.Create(company.Id, branch.Id, "Weekday shift", new() { Type = ShiftScheduleType.DailySameTime,
            DailyStart = TimeSpan.FromHours(23.5), DailyEnd = TimeSpan.FromHours(23.75) }, null, null, null);
        shift.CreatedUtc = Start.AddMonths(-1);
        var submitted = Form(); submitted.ShiftId = shift.Id; submitted.CompanyId = company.Id; submitted.BranchId = branch.Id;
        submitted.Submit(new() { new() { ItemText = "Corridor", IsChecked = true } }, null, "  Corridor cleaned  ", new() { new(Photo(SKColors.Blue, 200, 400), "Corridor photo note") });
        submitted.StartedAtUtc = Start; submitted.EndedAtUtc = Start.AddHours(2);
        var note = Form(); note.ShiftId = shift.Id; note.CompanyId = company.Id; note.BranchId = branch.Id;
        note.Submit(new() { new() { ItemText = "Corridor" } }, null, "Notes without a photo");
        note.StartedAtUtc = Start; note.EndedAtUtc = null;
        var draft = Form(); draft.ShiftId = shift.Id; draft.CompanyId = company.Id; draft.BranchId = branch.Id; draft.CleanerNotes = "DRAFT NOTE";
        var shifts = new Mock<IShiftRepository>(); var forms = new Mock<IQualityCycleRepository>();
        var companies = new Mock<ICompanyRepository>(); var branches = new Mock<IBranchRepository>();
        var profiles = new Mock<ITenantProfileRepository>(); var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns("tenant-a");
        profiles.Setup(x => x.GetAsync("tenant-a", default)).ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
        shifts.Setup(x => x.GetAsync(shift.Id, default)).ReturnsAsync(shift);
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, default)).ReturnsAsync(Array.Empty<ShiftAssignment>());
        companies.Setup(x => x.GetAsync(company.Id, default)).ReturnsAsync(company);
        branches.Setup(x => x.GetAsync(branch.Id, default)).ReturnsAsync(branch);
        forms.Setup(x => x.ListFormsAsync(shift.Id, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), default)).ReturnsAsync(new[] { submitted, note, draft });
        var service = new QualityCycleMonthlyReportService(shifts.Object, forms.Object, companies.Object, branches.Object, profiles.Object, context.Object, new Clock());
        var report = await service.BuildAsync(new(2026, 10, BranchId: branch.Id, ShiftId: shift.Id), default);
        var row = Assert.Single(report.Shifts); Assert.Equal(2, row.Evidence.Count);
        Assert.Equal(2m, row.RecordedStaffHours); Assert.Equal(1, row.IncompleteTimingRecords);
        Assert.Equal("Siivouskatu 12, 00100 Helsinki, Finland", row.BranchAddress);
        Assert.Contains(row.Evidence, x => x.Note == "Corridor cleaned" && x.Photos.Count == 1 && x.PhotoUrls.Count == 0 && x.StartUtc == Start);
        Assert.Contains(row.Evidence, x => x.Note == "Notes without a photo" && x.PhotoUrls.Count == 0);
        Assert.DoesNotContain(row.Evidence, x => x.Note == "DRAFT NOTE");
        forms.Verify(x => x.ListFormsAsync(shift.Id, new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 31, 22, 0, 0, DateTimeKind.Utc), default), Times.Once);
        QualityCycleReportShare? saved = null;
        forms.Setup(x => x.SaveReportShareAsync(It.IsAny<QualityCycleReportShare>(), default)).Callback<QualityCycleReportShare, CancellationToken>((share, _) => saved = share).Returns(Task.CompletedTask);
        await new CreateQualityCycleReportShareHandler(service, forms.Object, context.Object, new Clock()).Handle(new(2026, 10, ShiftId: shift.Id), default);
        Assert.Contains("Corridor cleaned", saved!.ReportJson); Assert.Contains("Notes without a photo", saved.ReportJson);
        Assert.Contains("data:image", saved.ReportJson); Assert.Contains("Corridor photo note", saved.ReportJson); Assert.DoesNotContain(submitted.Token, saved.ReportJson);
        Assert.DoesNotContain("PRIVATE CLEANER", saved.ReportJson);
        Assert.DoesNotContain(submitted.EmployeeId.ToString(), saved.ReportJson);
        Assert.DoesNotContain("DRAFT NOTE", saved.ReportJson);
        submitted.CleanerNotes = "CHANGED AFTER SHARE"; submitted.Photos[0] = new(submitted.Photos[0].Url, "CHANGED CAPTION");
        Assert.DoesNotContain("CHANGED AFTER SHARE", saved.ReportJson); Assert.DoesNotContain("CHANGED CAPTION", saved.ReportJson);
        var snapshot = JsonSerializer.Deserialize<QualityCycleMonthlyReport>(saved.ReportJson)!;
        Assert.Equal(2, snapshot.Shifts[0].Evidence.Count); Assert.Empty(snapshot.Shifts[0].Forms);
        Assert.Equal(row.RecordedStaffHours, snapshot.Shifts[0].RecordedStaffHours);
        Assert.Equal(row.IncompleteTimingRecords, snapshot.Shifts[0].IncompleteTimingRecords);
        Assert.Equal(row.WorkRecords, snapshot.Shifts[0].WorkRecords);
        Assert.Equal(2, Conversion.GetPageCount(new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(snapshot)));
        forms.Setup(x => x.GetReportShareByTokenAsync(saved.Token, default)).ReturnsAsync(saved);
        var sharedPdf = await new GetSharedQualityCycleReportPdfHandler(forms.Object, new QualityCyclePdfGenerator(), new Clock()).Handle(new(saved.Token), default);
        Assert.Equal(2, Conversion.GetPageCount(sharedPdf)); Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(sharedPdf));
    }

    [Fact]
    public async Task Monthly_pdf_embeds_portrait_and_landscape_images_and_note_only_entries()
    {
        var photos = new[] { Photo(SKColors.Blue, 200, 400), Photo(SKColors.Red, 600, 200) };
        var report = Report(new QualityCycleEvidenceReport(Start, Start.AddHours(2), "General shift note: equipment put away.", photos) {
                Photos = new[] { new QualityCyclePhoto(photos[0], "Corridor cleaned and inspected."), new QualityCyclePhoto(photos[1], "Gym floor cleaned and inspected.") } },
            new(Start.AddDays(1), Start.AddDays(1).AddHours(2), "Gym closed today. No photos were taken.", Array.Empty<string>()));
        var pdf = new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(report);
        Assert.Equal(2, Conversion.GetPageCount(pdf));
        var bluePixels = 0; var redPixels = 0;
        await foreach (var bitmap in Conversion.ToImagesAsync(pdf)) {
            using (bitmap) for (var y = 0; y < bitmap.Height; y += 8) for (var x = 0; x < bitmap.Width; x += 8) {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Blue > 200 && pixel.Red < 80 && pixel.Green < 80) bluePixels++;
                if (pixel.Red > 200 && pixel.Blue < 80 && pixel.Green < 80) redPixels++;
            }
        }
        Assert.True(bluePixels > 100); Assert.True(redPixels > 100);
        var output = Environment.GetEnvironmentVariable("QC_PHOTO_PDF_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) { Directory.CreateDirectory(Path.GetDirectoryName(output)!); await File.WriteAllBytesAsync(output, pdf); }
    }

    [Theory]
    [InlineData("https://example.com/private-photo.jpg")]
    [InlineData("file:///private/photo.png")]
    [InlineData("data:image/png;base64,invalid")]
    [InlineData("data:image/png;base64,Y29ycnVwdA==")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    public void Invalid_or_remote_historical_images_produce_an_appendix_instead_of_failing(string photo)
    {
        var pdf = new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(Report(new QualityCycleEvidenceReport(Start, Start.AddHours(2), "Historical note retained", new[] { photo })));
        Assert.Equal(2, Conversion.GetPageCount(pdf));
    }

    [Fact]
    public void Excessive_encoded_size_and_pixel_dimensions_do_not_break_report()
    {
        var oversized = "data:image/png;base64," + new string('A', 14 * 1024 * 1024);
        var huge = Photo(SKColors.Blue, 20_000, 1);
        var dense = PhotoWithDimensions(6500, 6500);
        var pdf = new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(Report(new QualityCycleEvidenceReport(Start, Start.AddHours(2), "Large historical photos", new[] { oversized, huge, dense })));
        Assert.Equal(2, Conversion.GetPageCount(pdf));
        var baseline = new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(Report());
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(baseline), "/Subtype /Image").Count * Conversion.GetPageCount(pdf),
            System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), "/Subtype /Image").Count); // only the embedded brand logo
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public void Other_supported_phone_photo_formats_are_embedded(SKEncodedImageFormat format, string mimeType)
    {
        var photo = Photo(SKColors.Blue, 200, 400, format, mimeType);
        var pdf = new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(Report(new QualityCycleEvidenceReport(Start, Start.AddHours(2), "Phone photo", new[] { photo })));
        Assert.Equal(2, Conversion.GetPageCount(pdf));
        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(pdf));
    }

    [Fact]
    public void Long_notes_paginate_and_older_reports_can_use_submitted_forms()
    {
        var form = Form(); form.Submit(new() { new() { ItemText = "Corridor" } }, null,
            string.Join("\n", Enumerable.Range(0, 180).Select(i => $"Observation {i}: detailed note about this shift.")));
        // Simulate a previously stored URL-only document, without rewriting history.
        form.PhotoUrls.Add(Photo(SKColors.Blue, 200, 400));
        var report = Report();
        report = report with { Shifts = new[] { report.Shifts[0] with { Forms = new[] { QualityCycleHandlers.MapForm(form) } } } };
        Assert.True(Conversion.GetPageCount(new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(report)) > 2);
        var legacy = new QualityCyclePdfGenerator().GenerateMonthlySummaryPdf("Weekday shift", "AAA", "AAA1", 2026, 10, new[] { QualityCycleHandlers.MapForm(form) });
        Assert.True(Conversion.GetPageCount(legacy) > 2);
    }

    [Fact]
    public void Old_serialized_reports_without_evidence_and_empty_reports_still_render()
    {
        var json = JsonSerializer.Serialize(Report());
        json = json.Replace(",\"Evidence\":[]", "");
        var restored = JsonSerializer.Deserialize<QualityCycleMonthlyReport>(json)!;
        Assert.Empty(restored.Shifts[0].Evidence);
        Assert.Equal(1, Conversion.GetPageCount(new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(restored)));
        Assert.Equal(1, Conversion.GetPageCount(new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(restored with { Shifts = Array.Empty<QualityCycleShiftReport>() })));
    }

    private static QualityCycleMonthlyReport Report(params QualityCycleEvidenceReport[] evidence) => new(2026, 10, "Europe/Helsinki", Start,
        new[] { new QualityCycleShiftReport(Guid.NewGuid(), "Weekday shift", Guid.NewGuid(), "AAA", Guid.NewGuid(), "AAA1",
            1, 2m, 1, 0, 0, 1, 1, 0, 0, new[] { new QualityCycleOccurrenceReport(Start, Start.AddHours(2), 1, 1, "Recorded") },
            Array.Empty<QualityCycleFormDto>(), new[] { new QualityCycleItemReport("Corridor", 1, 1, new[] { "2026-10-02" }) }) { Evidence = evidence } });

    private static string Photo(SKColor color, int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png, string mimeType = "image/png")
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(format, 100);
        return $"data:{mimeType};base64," + Convert.ToBase64String(data.ToArray());
    }

    private static string PhotoWithDimensions(int width, int height)
    {
        var bytes = Convert.FromBase64String(Photo(SKColors.Blue, 1, 1).Split(',')[1]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        uint crc = uint.MaxValue;
        foreach (var value in bytes.AsSpan(12, 17)) {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ~crc);
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
        Assert.Equal(width, codec.Info.Width); Assert.Equal(height, codec.Info.Height);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
