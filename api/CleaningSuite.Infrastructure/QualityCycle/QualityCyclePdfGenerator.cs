using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing.Exceptions;
using SkiaSharp;

namespace CleaningSuite.Infrastructure.QualityCycle;

public class QualityCyclePdfGenerator : IQualityCyclePdfGenerator
{
    public byte[] GenerateMonthlyReportPdf(QualityCycleMonthlyReport report)
    {
        var zone = TimeZoneConverter.TZConvert.GetTimeZoneInfo(report.TimeZoneId);
        string T(string text) => ReportText.Get(report.Language, text);
        var period = report.Periods is { Count: > 0 } ? string.Join("; ", report.Periods.Select(p => $"{p.From:dd/MM/yyyy} - {p.To:dd/MM/yyyy}")) : $"{report.Year}-{report.Month:D2}";
        string Time(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), zone).ToString("yyyy-MM-dd HH:mm");
        var document = Document.Create(container => {
            if (report.Shifts.Count == 0)
                container.Page(page => {
                    ReportPdfTheme.Page(page);
                    page.Header().Element(x => ReportPdfTheme.Header(x, T("Cleaning Quality Report"), $"{period} | {report.TimeZoneId}"));
                    page.Content().PaddingTop(20).Text(T("No scheduled checklist shifts in this month."));
                    page.Footer().Element(x => ReportPdfTheme.Footer(x, report.GeneratedUtc, report.Language));
                });
            foreach (var shift in report.Shifts)
                container.Page(page => {
                    ReportPdfTheme.Page(page);
                    var reference = $"{report.Year}{report.Month:D2}-{shift.CompanyId.ToString("N")[..6]}-{shift.BranchId.ToString("N")[..6]}-{shift.ShiftId.ToString("N")[..6]}";
                    page.Header().Element(x => ReportPdfTheme.Header(x, T("Cleaning Quality Report"), $"{period} | {report.TimeZoneId} | Ref. {reference}"));
                    page.Content().PaddingVertical(14).Column(col => {
                        col.Spacing(10);
                        col.Item().Element(x => ReportPdfTheme.Band(x, T("PROPERTY & SERVICE")));
                        col.Item().Element(ReportPdfTheme.Cell).Column(details => {
                            details.Spacing(3);
                            details.Item().Text($"{T("Company")}: {shift.CompanyName}").SemiBold();
                            details.Item().Text($"{T("Branch")}: {shift.BranchName}");
                            details.Item().Text($"{T("Address")}: {(string.IsNullOrWhiteSpace(shift.BranchAddress) ? T("Not recorded") : shift.BranchAddress)}");
                            details.Item().Text($"{T("Shift")}: {shift.ShiftName}");
                        });
                        col.Item().Element(x => ReportPdfTheme.Band(x, T(report.Periods is { Count: > 0 } ? "REPORT SUMMARY" : "MONTHLY SUMMARY")));
                        col.Item().Background(ReportPdfTheme.Pale).Padding(10).Column(summary => {
                            summary.Spacing(4);
                            summary.Item().Text($"{T("Scheduled occurrences")}: {shift.ScheduledOccurrences} | {T("Scheduled hours")}: {shift.ScheduledHours:0.##}");
                            summary.Item().Text($"{T("Recorded occurrences")}: {shift.CompletedOccurrences} | {T("Missing")}: {shift.MissingOccurrences} | {T("Pending")}: {shift.PendingOccurrences}");
                            summary.Item().Text($"{T("Cleaner tasks")}: {shift.SubmittedTasks}/{shift.TotalTasks} {T("submitted")}");
                            summary.Item().Text($"{T("Recorded staff hours")}: {(shift.RecordedStaffHours.HasValue ? shift.RecordedStaffHours.Value.ToString("0.##") : T("Not available"))}").SemiBold();
                            if (shift.IncompleteTimingRecords is > 0) summary.Item().Text($"{T("Incomplete timing records")}: {shift.IncompleteTimingRecords}");
                            summary.Item().Text(T("Scheduled hours describe visits. Recorded staff hours sum each cleaner’s actual submitted duration; incomplete timings are excluded. Missing means no submission, not a failed cleaning activity.")).FontSize(8);
                        });
                        col.Item().Element(x => ReportPdfTheme.Band(x, T("ACTIVITIES PERFORMED")));
                        col.Item().Text(T("Activities are measured by recorded visits and distinct days. Unchecked items are not failures.")).FontSize(8);
                        col.Item().Table(table => {
                            table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(); });
                            table.Header(h => { h.Cell().Element(ReportPdfTheme.Head).Text(T("Activity")); h.Cell().Element(ReportPdfTheme.Head).Text(T("Days")); h.Cell().Element(ReportPdfTheme.Head).Text(T("Occurrences")); });
                            foreach (var item in shift.Items) {
                                table.Cell().Element(ReportPdfTheme.Cell).Text(item.ItemText);
                                table.Cell().Element(ReportPdfTheme.Cell).Text(item.DaysPerformed.ToString());
                                table.Cell().Element(ReportPdfTheme.Cell).Text(item.RecordedOccurrences.ToString());
                            }
                        });
                        col.Item().Element(x => ReportPdfTheme.Band(x, T("VISIT RECORD")));
                        col.Item().Table(table => {
                            table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                            table.Header(h => { foreach (var label in new[] { "Scheduled date / start", "Hours", "Submitted / expected", "Status" }) h.Cell().Element(ReportPdfTheme.Head).Text(T(label)); });
                            foreach (var visit in shift.Occurrences) {
                                table.Cell().Element(ReportPdfTheme.Cell).Text(Time(visit.StartUtc));
                                table.Cell().Element(ReportPdfTheme.Cell).Text((visit.EndUtc - visit.StartUtc).TotalHours.ToString("0.##"));
                                table.Cell().Element(ReportPdfTheme.Cell).Text($"{visit.SubmittedTasks}/{visit.ExpectedTasks}");
                                table.Cell().Element(ReportPdfTheme.Cell).Text(T(visit.Status));
                            }
                        });
                        col.Item().EnsureSpace(150).Column(actual => {
                        actual.Spacing(6);
                        actual.Item().Element(x => ReportPdfTheme.Band(x, T("ACTUAL WORK TIMES")));
                        if (shift.WorkRecords is not { Count: > 0 }) actual.Item().Text(T("Actual work times: Not available for this report snapshot.")).FontSize(8);
                        else actual.Item().Table(table => {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(0.6f); });
                            table.Header(h => { foreach (var label in new[] { "Scheduled visit", "Actual start", "Actual end", "Staff hours" }) h.Cell().Element(ReportPdfTheme.Head).Text(T(label)); });
                            foreach (var work in shift.WorkRecords) {
                                table.Cell().Element(ReportPdfTheme.Cell).Text(Time(work.ScheduledStartUtc));
                                table.Cell().Element(ReportPdfTheme.Cell).Text(work.ActualStartUtc.HasValue ? Time(work.ActualStartUtc.Value) : T("Not available"));
                                table.Cell().Element(ReportPdfTheme.Cell).Text(work.ActualEndUtc.HasValue ? Time(work.ActualEndUtc.Value) : T("Not available"));
                                table.Cell().Element(ReportPdfTheme.Cell).Text(work.StaffHours?.ToString("0.##") ?? T("Not available"));
                            }
                        });
                        });
                    });
                    page.Footer().Element(x => ReportPdfTheme.Footer(x, report.GeneratedUtc, report.Language));
                });
            foreach (var shift in report.Shifts)
                AppendEvidence(container, shift.CompanyName, shift.BranchName, shift.ShiftName,
                    EvidenceFor(shift), zone, report.TimeZoneId, report.GeneratedUtc, report.Language);
        });
        return document.GeneratePdf();
    }

    static QualityCyclePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static IReadOnlyList<QualityCycleEvidenceReport> EvidenceFor(QualityCycleShiftReport shift) =>
        shift.Evidence is { Count: > 0 } ? shift.Evidence : ProjectEvidence(shift.Forms);

    private static IReadOnlyList<QualityCycleEvidenceReport> ProjectEvidence(IReadOnlyList<QualityCycleFormDto> forms) =>
        forms.Where(f => f.IsSubmitted && (!string.IsNullOrWhiteSpace(f.CleanerNotes) || f.PhotoUrls is { Count: > 0 }))
            .OrderBy(f => f.ShiftOccurrenceUtc).ThenBy(f => f.Id)
            .Select(f => new QualityCycleEvidenceReport(f.ShiftOccurrenceUtc, f.ShiftOccurrenceEndUtc,
                f.CleanerNotes, f.PhotoUrls ?? new List<string>()) { Photos = f.Photos }).ToList();

    private static void AppendEvidence(IDocumentContainer container, string companyName, string branchName,
        string shiftName, IReadOnlyList<QualityCycleEvidenceReport> evidence, TimeZoneInfo zone, string zoneName, DateTime? generatedUtc = null, string language = "en")
    {
        string T(string text) => ReportText.Get(language, text);
        foreach (var entry in evidence.OrderBy(e => e.StartUtc))
        {
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(entry.StartUtc, DateTimeKind.Utc), zone);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(entry.EndUtc, DateTimeKind.Utc), zone);
            var paired = entry.Photos is { Count: > 0 } ? entry.Photos
                : entry.PhotoUrls.Select(url => new QualityCyclePhoto(url, entry.Note ?? "")).ToList();
            var hasPhotos = paired.Count > 0;
            var photos = !hasPhotos ? new QualityCyclePhoto?[] { null } : paired.Cast<QualityCyclePhoto?>();
            var index = 0;
            foreach (var photo in photos)
            {
                index++;
                var image = photo is null ? null : DecodePhoto(photo.Url);
                var photoNumber = index;
                container.Page(page =>
                {
                    ReportPdfTheme.Page(page);
                    page.Header().Column(header =>
                    {
                        header.Spacing(4);
                        header.Item().Element(x => ReportPdfTheme.Header(x, T("Photos & Notes"), T("Appendix - shift photos and notes")));
                        header.Item().Text($"{companyName} / {branchName} / {shiftName}").SemiBold();
                        header.Item().Text($"{T("Shift date")}: {localStart:yyyy-MM-dd} | {localStart:HH:mm} - {localEnd:yyyy-MM-dd HH:mm} ({zoneName})");
                        if (hasPhotos) header.Item().Text($"{T("Photo")} {photoNumber} {T("of")} {paired.Count}").FontSize(9);
                    });
                    page.Content().PaddingVertical(15).Column(content =>
                    {
                        content.Spacing(10);
                        var note = photo?.Note ?? entry.Note;
                        content.Item().Text(hasPhotos ? T("Photo note") : T("Shift note")).SemiBold();
                        content.Item().Text(string.IsNullOrWhiteSpace(note) ? T("No note recorded for this historical entry.") : note);
                        if (entry.Photos is { Count: > 0 } && !string.IsNullOrWhiteSpace(entry.Note)) {
                            content.Item().Text(T("Shift note")).SemiBold(); content.Item().Text(entry.Note);
                        }
                        if (!hasPhotos) return;
                        if (image is null)
                            content.Item().Text(T("Photo unavailable: the saved image is unsupported, invalid or exceeds the report image limit."))
                                .FontColor(Colors.Grey.Darken1).Italic();
                        else
                            content.Item().EnsureSpace(360).Height(360).Image(image).FitArea();
                    });
                    page.Footer().Element(x => ReportPdfTheme.Footer(x, generatedUtc ?? DateTime.UtcNow, language));
                });
            }
        }
    }

    private static QuestPDF.Infrastructure.Image? DecodePhoto(string value)
    {
        const int maximumBytes = 10 * 1024 * 1024;
        if (value.Length > (maximumBytes + 2) / 3 * 4 + 64 || !value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return null;
        var comma = value.IndexOf(',');
        if (comma < 0) return null;
        var header = value[..comma].ToLowerInvariant();
        if (header is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/jpg;base64" or
            "data:image/webp;base64" or "data:image/bmp;base64")) return null;
        try
        {
            var bytes = Convert.FromBase64String(value[(comma + 1)..]);
            if (bytes.Length == 0 || bytes.Length > maximumBytes) return null;
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                codec.Info.Width > 16_384 || codec.Info.Height > 16_384 ||
                (long)codec.Info.Width * codec.Info.Height > 40_000_000) return null;
            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null) return null;
            return QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
        }
        catch (FormatException) { return null; }
        catch (DocumentComposeException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public byte[] GenerateMonthlySummaryPdf(
        string shiftName,
        string companyName,
        string branchName,
        int year,
        int month,
        IReadOnlyList<QualityCycleFormDto> forms)
    {
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                ReportPdfTheme.Page(page);

                page.Header().Element(header => ReportPdfTheme.Header(header, "Cleaning Quality Report", $"Monthly Summary Report - {monthName} | UTC"));

                page.Content().PaddingVertical(15).Column(col =>
                {
                    // Details Table / Summary Box
                    col.Item().Background(Colors.Grey.Lighten4).Padding(12).Column(box =>
                    {
                        box.Item().Text($"Shift: {shiftName}").Bold().FontSize(11);
                        box.Item().Text($"Company: {companyName} | Branch: {branchName}");
                        box.Item().Text($"Total Shift Occurrences Logged: {forms.Count}");

                        int completedForms = forms.Count(f => f.IsSubmitted);
                        double completionRate = forms.Count > 0 ? (double)completedForms / forms.Count * 100 : 0;
                        box.Item().Text($"Completed Forms: {completedForms}/{forms.Count} ({completionRate:F0}%)").Bold().FontColor(completionRate == 100 ? Colors.Green.Darken2 : Colors.Orange.Darken2);
                    });

                    col.Item().Height(15);

                    if (forms.Count == 0)
                    {
                        col.Item().Text("No Quality Cycle form submissions recorded for this month.").Italic();
                        return;
                    }

                    // Form Occurrences List
                    foreach (var form in forms.OrderBy(f => f.ShiftOccurrenceUtc))
                    {
                        col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(formBox =>
                        {
                            formBox.Item().Row(r =>
                            {
                                r.RelativeItem().Text($"Date: {form.ShiftOccurrenceUtc:yyyy-MM-dd HH:mm UTC}").Bold();
                                r.RelativeItem().Text($"Cleaner: {form.EmployeeName}").SemiBold();
                                r.ConstantItem(100).AlignRight().Text(form.IsSubmitted ? "COMPLETED" : "PENDING")
                                    .Bold()
                                    .FontColor(form.IsSubmitted ? Colors.Green.Medium : Colors.Red.Medium);
                            });

                            if (form.IsSubmitted)
                            {
                                formBox.Item().PaddingTop(6).Text("Checklist Items:").SemiBold().FontSize(9);

                                int checkedCount = form.Items.Count(i => i.IsChecked);
                                formBox.Item().Text($"Completed {checkedCount} / {form.Items.Count} items").FontSize(9).FontColor(Colors.Grey.Darken1);

                                formBox.Item().PaddingTop(4).Column(itemsBox =>
                                {
                                    foreach (var item in form.Items)
                                    {
                                        var checkSymbol = item.IsChecked ? "[X]" : "[  ]";
                                        itemsBox.Item().Text($"{checkSymbol} {item.ItemText}")
                                            .FontSize(9)
                                            .FontColor(item.IsChecked ? Colors.Grey.Darken3 : Colors.Grey.Medium);
                                    }
                                });

                                if (!string.IsNullOrWhiteSpace(form.CleanerNotes))
                                {
                                    formBox.Item().PaddingTop(6).Text($"Cleaner Notes: {form.CleanerNotes}").Italic().FontSize(9);
                                }

                                if (form.PhotoUrls != null && form.PhotoUrls.Count > 0)
                                {
                                    formBox.Item().PaddingTop(4).Text($"Attached Photos: {form.PhotoUrls.Count} photo(s) attached").FontSize(9).FontColor(Colors.Blue.Darken1);
                                }
                            }
                        });

                        col.Item().Height(10);
                    }
                });

                page.Footer().Element(x => ReportPdfTheme.Footer(x, DateTime.UtcNow));
            });
            AppendEvidence(container, companyName, branchName, shiftName, ProjectEvidence(forms), TimeZoneInfo.Utc, "UTC");
        });

        return doc.GeneratePdf();
    }
}
