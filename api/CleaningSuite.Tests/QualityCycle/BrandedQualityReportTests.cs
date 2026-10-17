using System.Text.Json;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.QualityCycle;
using PDFtoImage;

namespace CleaningSuite.Tests.QualityCycle;

public class BrandedQualityReportTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static QualityCycleFormDto Form(bool submitted = true, bool ended = true)
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Weekday cleaning", Guid.NewGuid(), "PRIVATE CLEANER", Guid.NewGuid(), "Activities", Start, ["Corridor"], Start.AddHours(2));
        form.StartedAtUtc = Start.AddDays(16); form.EndedAtUtc = ended ? Start.AddDays(16).AddHours(2) : null;
        form.IsSubmitted = submitted;
        return QualityCycleHandlers.MapForm(form);
    }
    [Fact]
    public void Staff_hours_sum_individual_durations_and_deduplicate_forms()
    {
        var first = Form(); var second = Form();
        var work = QualityCycleWorkProjection.FromForms([first, second, first]);
        Assert.Equal(4m, work.StaffHours); Assert.Equal(0, work.IncompleteRecords); Assert.Equal(2, work.Records.Count);
        var json = JsonSerializer.Serialize(work);
        Assert.DoesNotContain("PRIVATE CLEANER", json); Assert.DoesNotContain(first.EmployeeId.ToString(), json);
        Assert.DoesNotContain(first.Id.ToString(), json); Assert.DoesNotContain(first.Token, json);
    }
    [Fact]
    public void Partial_missing_and_invalid_timings_are_explicit_and_not_zero_hours()
    {
        var valid = Form(); var incomplete = Form(ended: false);
        var partial = QualityCycleWorkProjection.FromForms([valid, incomplete]);
        Assert.Equal(2m, partial.StaffHours); Assert.Equal(1, partial.IncompleteRecords);
        var invalid = valid with { EndedAtUtc = valid.StartedAtUtc!.Value.AddMinutes(-1) };
        var missing = incomplete with { StartedAtUtc = null };
        var unavailable = QualityCycleWorkProjection.FromForms([invalid, missing]);
        Assert.Null(unavailable.StaffHours); Assert.Equal(2, unavailable.IncompleteRecords);
        Assert.Null(QualityCycleWorkProjection.FromForms([]).StaffHours);
    }
    [Fact]
    public async Task Reports_render_timing_address_long_tables_empty_and_legacy_snapshots()
    {
        var forms = new[] { Form(), Form(ended: false) }; var work = QualityCycleWorkProjection.FromForms(forms);
        var shift = new QualityCycleShiftReport(Guid.NewGuid(), "Weekday cleaning / Viikkosiivous", Guid.NewGuid(), "Methehe Oy", Guid.NewGuid(), "Helsinki - päätoimisto",
            1, 2m, 0, 0, 1, 2, 1, 0, 1, [new(Start, Start.AddHours(2), 2, 1, "Pending")], forms,
            [new("Käytävän puhdistus / Corridor", 12, 12, [])]) { BranchAddress = "Siivouskatu 12, 00100 Helsinki, Suomi",
                WorkRecords = work.Records, RecordedStaffHours = work.StaffHours, IncompleteTimingRecords = work.IncompleteRecords };
        var report = new QualityCycleMonthlyReport(2026, 10, "Europe/Helsinki", Start.AddDays(16), [shift]);
        var pdf = new QualityCyclePdfGenerator();
        var examples = new Dictionary<string, QualityCycleMonthlyReport> {
            ["timing"] = report,
            ["empty"] = report with { Shifts = [] },
            ["legacy"] = report with { Shifts = [shift with { Forms = [], WorkRecords = null, RecordedStaffHours = null, IncompleteTimingRecords = null, BranchAddress = null }] },
            ["long"] = report with { Shifts = [shift with {
                ShiftName = string.Join(" ", Enumerable.Repeat("Long service name / palvelun nimi", 8)),
                Items = Enumerable.Range(1, 95).Select(i => new QualityCycleItemReport($"Activity {i}: detailed Finnish and English cleaning instructions / puhdistusohjeet", 12, 12, [])).ToList(),
                Occurrences = Enumerable.Range(0, 31).Select(i => new QualityCycleOccurrenceReport(Start.AddDays(i), Start.AddDays(i).AddHours(2), 2, 1, "Missing")).ToList()
            }] }
        };
        var directory = Environment.GetEnvironmentVariable("QC_BRAND_PDF_TEST_DIR");
        foreach (var (name, example) in examples)
        {
            var bytes = pdf.GenerateMonthlyReportPdf(example);
            Assert.True(Conversion.GetPageCount(bytes) >= (name == "long" ? 3 : 1));
            Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(bytes));
            if (!string.IsNullOrWhiteSpace(directory)) { Directory.CreateDirectory(directory); await File.WriteAllBytesAsync(Path.Combine(directory, name + ".pdf"), bytes); }
        }
    }
}
