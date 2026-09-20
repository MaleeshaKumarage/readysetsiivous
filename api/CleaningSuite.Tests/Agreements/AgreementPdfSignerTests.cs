using CleaningSuite.Domain.Agreements;
using CleaningSuite.Infrastructure.Agreements;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CleaningSuite.Tests.Agreements;

public class AgreementPdfSignerTests
{
    [Fact]
    public async Task Generate_Produces_SamePageCount_Pdf()
    {
        // Uses a minimal 2-page PDF produced inline via QuestPDF.
        var original = MakeTwoPagePdf();
        var outPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        var signers = new List<Signer>
        {
            new() { Name = "Alice", TypedName = "Alice", SignedAtUtc = DateTime.UtcNow, SignatureImagePath = MakePng() },
        };

        var gen = new AgreementPdfSigner();
        await gen.GenerateSignedPdfAsync(original, signers, outPath, CancellationToken.None);

        var outBytes = await File.ReadAllBytesAsync(outPath);
        var pages = PDFtoImage.Conversion.GetPageCount(outBytes, password: null);
        Assert.Equal(2, pages);
        File.Delete(original);
        File.Delete(outPath);
    }

    private static string MakeTwoPagePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        QuestPDF.Fluent.Document.Create(c =>
        {
            c.Page(p => p.Content().Text("page1"));
            c.Page(p => p.Content().Text("page2"));
        }).GeneratePdf(path);
        return path;
    }

    private static string MakePng()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        using var bmp = new SkiaSharp.SKBitmap(200, 60);
        using (var canvas = new SkiaSharp.SKCanvas(bmp))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, IsAntialias = true };
            using var font = new SkiaSharp.SKFont { Size = 16 };
            canvas.DrawText("Alice", 10, 40, SkiaSharp.SKTextAlign.Left, font, paint);
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
        return path;
    }
}
