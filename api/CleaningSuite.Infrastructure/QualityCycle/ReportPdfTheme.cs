using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CleaningSuite.Infrastructure.QualityCycle;

internal static class ReportPdfTheme
{
    internal const string Navy = "#0a1741", Gold = "#ddc067", Pale = "#f3f5fa", Line = "#c6cee3";
    private static readonly byte[] Logo = ReadLogo();
    private static byte[] ReadLogo()
    {
        using var stream = typeof(ReportPdfTheme).Assembly.GetManifestResourceStream("ReadySetSiivous.ReportLogo")
            ?? throw new InvalidOperationException("The report logo resource is missing.");
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
    internal static void Page(PageDescriptor page)
    {
        page.Size(PageSizes.A4); page.Margin(36);
        page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial).FontColor(Navy));
    }
    internal static void Header(IContainer container, string title, string subtitle)
    {
        container.BorderBottom(2).BorderColor(Gold).PaddingBottom(10).Row(row => {
            row.ConstantItem(66).Height(66).Image(Logo).FitArea();
            row.RelativeItem().PaddingLeft(14).AlignMiddle().Column(col => {
                col.Item().Text("READYSET SIIVOUS").FontSize(9).SemiBold();
                col.Item().Text(title).FontSize(19).Bold();
                col.Item().PaddingTop(4).Text(subtitle).FontSize(8);
            });
        });
    }
    internal static void Footer(IContainer container, DateTime generated)
    {
        container.BorderTop(1).BorderColor(Line).PaddingTop(6).Row(row => {
            row.RelativeItem().Text($"Generated {generated:yyyy-MM-dd HH:mm} UTC").FontSize(8);
            row.AutoItem().Text(text => { text.DefaultTextStyle(x => x.FontSize(8)); text.Span("Page "); text.CurrentPageNumber(); text.Span(" of "); text.TotalPages(); });
        });
    }
    internal static IContainer Cell(IContainer container) => container.EnsureSpace(42).Border(0.5f).BorderColor(Line).Padding(6);
    internal static IContainer Head(IContainer container) => container.Background(Gold).Border(0.5f).BorderColor(Line).Padding(6).DefaultTextStyle(x => x.SemiBold());
    internal static void Band(IContainer container, string label) => container.EnsureSpace(50).Background(Navy).Padding(7).Text(label).FontColor(Colors.White).SemiBold();
}
