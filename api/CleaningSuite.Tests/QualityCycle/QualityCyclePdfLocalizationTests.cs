using System.Globalization;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.QualityCycle;
using UglyToad.PdfPig;

namespace CleaningSuite.Tests.QualityCycle;

// QuestPDF 2024 has a Windows-only concurrent font-subsetting issue. Run text
// assertions without unrelated PDF generation, rather than accepting corrupt text.
[CollectionDefinition("PDF text verification", DisableParallelization = true)]
public class PdfTextVerificationCollection { }

[Collection("PDF text verification")]
public class QualityCyclePdfLocalizationTests
{
    private static byte[] AppendixPdf(string language, IReadOnlyList<QualityCycleEvidenceReport> evidence)
    {
        var row = new QualityCycleShiftReport(Guid.NewGuid(), "Vuoro", Guid.NewGuid(), "Yritys Oy", Guid.NewGuid(), "Kohde",
            0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], []) { Evidence = evidence };
        return new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(new(2026, 10, "Europe/Helsinki", Start, [row]) { Language = language });
    }

    private static string SamplePhoto(int index)
    {
        using var bitmap = new SkiaSharp.SKBitmap(index % 2 == 0 ? 160 : 90, index % 2 == 0 ? 90 : 160);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(new SkiaSharp.SKColor((byte)(40 + index * 20), 100, 170));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
    }

    [Theory]
    [InlineData("en", 4)] [InlineData("fi", 4)] [InlineData("sv", 4)]
    [InlineData("en", 5)] [InlineData("fi", 5)] [InlineData("sv", 5)]
    public void Appendix_packs_four_photos_across_dates_and_keeps_odd_remainder(string language, int count)
    {
        var evidence = Enumerable.Range(0, count).Select(i => new QualityCycleEvidenceReport(Start.AddDays(i), Start.AddDays(i).AddHours(2), null, [])
            { Photos = [new(SamplePhoto(i), $"Photo-note-{i + 1} unchanged ää ö")] }).ToArray();
        var pdf = AppendixPdf(language, evidence);
        SaveQa($"compact-appendix-{language}-{count}", pdf);
        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages().Where(p => string.Join(" ", p.GetWords().Select(w => w.Text)).Contains(ReportText.Get(language, "Photos & Notes"))).ToArray();
        Assert.Equal((count + 3) / 4, pages.Length);
        // One brand logo plus the actual evidence images on each appendix page.
        Assert.Equal(5, pages[0].NumberOfImages);
        if (count == 5) Assert.Equal(2, pages[1].NumberOfImages);
        for (var i = 0; i < count; i++)
        {
            var text = string.Join(" ", pages[i / 4].GetWords().Select(w => w.Text));
            Assert.Contains($"Photo-note-{i + 1} unchanged ää ö", text);
            Assert.Contains(Start.AddDays(i).ToString("yyyy-MM-dd"), text);
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("fi")] [InlineData("sv")]
    public void Appendix_keeps_full_long_notes_and_prints_general_note_once(string language)
    {
        var note = string.Join(" ", Enumerable.Range(0, 160).Select(i => $"Detail{i} corridor cleaned.")) + " FULL-NOTE-END";
        var pdf = AppendixPdf(language, [new(Start, Start.AddHours(2), "GENERAL-NOTE-ONCE", []) {
            Photos = [new(SamplePhoto(0), note), new(SamplePhoto(1), "Second-photo-note")] },
            new(Start.AddDays(1), Start.AddDays(1).AddHours(2), "NOTES-WITHOUT-PHOTO", [])]);
        SaveQa($"compact-long-notes-{language}", pdf);
        var text = Extract(pdf);
        Assert.Contains("FULL-NOTE-END", text);
        Assert.Contains("Second-photo-note", text);
        Assert.Contains("NOTES-WITHOUT-PHOTO", text);
        Assert.Equal(1, text.Split("GENERAL-NOTE-ONCE").Length - 1);
        foreach (var i in Enumerable.Range(0, 160)) Assert.Contains($"Detail{i} ", text);
    }

    private static readonly DateTime Start = new(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly string[] EnglishSystemPhrases = [
        "Cleaning Quality Report", "PROPERTY & SERVICE", "REPORT SUMMARY", "MONTHLY SUMMARY",
        "Company:", "Branch:", "Address:", "Shift:", "Reference ", "Scheduled occurrences:", "Scheduled hours:",
        "Recorded occurrences:", "Missing:", "Pending:", "Cleaner tasks:", "submitted", "Recorded staff hours:",
        "Incomplete timing records:", "Scheduled hours describe visits.", "ACTIVITIES PERFORMED",
        "Activities are measured by recorded visits", "Activity", "Days", "Occurrences", "VISIT RECORD",
        "Scheduled date / start", "Hours", "Submitted / expected", "Recorded", "Missing", "Pending",
        "ACTUAL WORK TIMES", "Actual work times:", "Scheduled visit", "Actual start", "Actual end", "Staff hours",
        "Photos & Notes", "Appendix - shift photos and notes", "Shift date:", "Photo note", "Shift note",
        "No note recorded for this historical entry.", "Photo unavailable:", "Generated ", "Page ",
        "Monthly Summary Report", "Total Shift Occurrences Logged:", "Completed Forms:",
        "No Quality Cycle form submissions recorded for this month.", "Date:", "Cleaner:", "COMPLETED", "PENDING",
        "Checklist Items:", "Cleaner Notes:", "Attached Photos:", "photo(s) attached", "Not available", "Not recorded",
    ];
    private static string Extract(byte[] pdf) {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }
    private static void SaveQa(string name, byte[] pdf) {
        var directory = Environment.GetEnvironmentVariable("QC_LOCALIZED_PDF_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf);
    }
    private static void AssertLocalized(string language, byte[] pdf) {
        var text = Extract(pdf);
        Assert.DoesNotContain('\0', text);
        foreach (var phrase in EnglishSystemPhrases) Assert.DoesNotContain(phrase, text, StringComparison.Ordinal);
        Assert.Contains(language == "fi" ? "Siivouksen laaturaportti" : "Kvalitetsrapport för städning", text);
        Assert.Contains(language == "fi" ? "Luotu" : "Skapad", text);
        Assert.Contains(language == "fi" ? "Sivu" : "Sida", text);
    }

    [Theory]
    [InlineData("fi")] [InlineData("sv")]
    public void Summary_and_appendix_translate_statuses_fallbacks_captions_and_footer(string language)
    {
        var evidence = new[] {
            new QualityCycleEvidenceReport(Start, Start.AddHours(2), "", []) { Photos = [new("data:image/png;base64,invalid", "")] },
            new QualityCycleEvidenceReport(Start.AddDays(1), Start.AddDays(1).AddHours(2), "", [])
        };
        var row = new QualityCycleShiftReport(Guid.NewGuid(), "Vuoro A", Guid.NewGuid(), "Yritys Oy", Guid.NewGuid(), "Kohde A",
            3, 6m, 1, 1, 1, 3, 1, 1, 1,
            [new(Start, Start.AddHours(2), 1, 1, "Recorded"), new(Start.AddDays(1), Start.AddDays(1).AddHours(2), 1, 0, "Missing"), new(Start.AddDays(2), Start.AddDays(2).AddHours(2), 1, 0, "Pending")], [],
            [new("Käytävä", 1, 1, ["2026-10-02"])]) {
            RecordedStaffHours = null, IncompleteTimingRecords = 1, Evidence = evidence,
            WorkRecords = [new(Start, Start.AddHours(2), null, null, null)]
        };
        var report = new QualityCycleMonthlyReport(2026, 10, "Europe/Helsinki", Start, [row, row with { ShiftId = Guid.NewGuid(), WorkRecords = null, Evidence = [] }]) {
            Language = language, Periods = [new(new(2026, 10, 1), new(2026, 10, 31))]
        };
        var pdf = new QualityCyclePdfGenerator().GenerateMonthlyReportPdf(report);
        SaveQa($"quality-report-system-audit-{language}", pdf);
        AssertLocalized(language, pdf);
        var text = Extract(pdf);
        foreach (var key in new[] { "Reference", "Photo", "Photo note", "Shift note", "Photo unavailable: the saved image is unsupported, invalid or exceeds the report image limit.", "No note recorded for this historical entry.", "Not available", "Incomplete timing records" })
            Assert.Contains(ReportText.Get(language, key), text);
    }

    [Theory]
    [InlineData("fi")] [InlineData("sv")]
    public void Empty_and_legacy_summaries_translate_all_system_text_and_month_names(string language)
    {
        var generator = new QualityCyclePdfGenerator();
        AssertLocalized(language, generator.GenerateMonthlyReportPdf(new(2026, 10, "Europe/Helsinki", Start, []) { Language = language }));
        AssertLocalized(language, generator.GenerateMonthlySummaryPdf("Vuoro", "Yritys", "Kohde", 2026, 10, [], language));
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Vuoro", Guid.NewGuid(), "Matti", Guid.NewGuid(), "Lista", Start, ["Käytävä"], Start.AddHours(2));
        form.Submit([new() { ItemText = "Käytävä", IsChecked = true }], null, "Puhdas", [new("data:image/png;base64,invalid", "Puhdas lattia")], Start.AddHours(2));
        var pending = QualityCycleForm.Create(form.ShiftId, "Vuoro", Guid.NewGuid(), "Anna", form.TemplateId, "Lista", Start.AddDays(1), ["Käytävä"]);
        var pdf = generator.GenerateMonthlySummaryPdf("Vuoro", "Yritys", "Kohde", 2026, 10, [QualityCycleHandlers.MapForm(form), QualityCycleHandlers.MapForm(pending)], language);
        SaveQa($"quality-report-legacy-audit-{language}", pdf);
        AssertLocalized(language, pdf);
        var text = Extract(pdf);
        Assert.DoesNotContain("October", text);
        Assert.Contains(new DateTime(2026, 10, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo(language == "fi" ? "fi-FI" : "sv-SE")), text);
    }

    [Theory]
    [InlineData("fi")] [InlineData("sv")]
    public void Missing_translation_cannot_silently_fall_back_to_English(string language) =>
        Assert.Throws<InvalidOperationException>(() => ReportText.Get(language, "A newly added untranslated label"));
}
