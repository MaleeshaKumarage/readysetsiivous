using CleaningSuite.Application.QualityCycle;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CleaningSuite.Infrastructure.QualityCycle;

public class QualityCyclePdfGenerator : IQualityCyclePdfGenerator
{
    static QualityCyclePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
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
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Header().Element(header =>
                {
                    header.Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("ReadySetSiivous Quality Cycle Report").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                            col.Item().Text($"Monthly Summary Report — {monthName}").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                        });

                        row.ConstantItem(120).AlignRight().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd}").FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                });

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

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });

        return doc.GeneratePdf();
    }
}
