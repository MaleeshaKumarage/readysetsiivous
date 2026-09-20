using CleaningSuite.Domain.Agreements;
using CleaningSuite.Infrastructure.Agreements;
using PDFtoImage;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace CleaningSuite.Tests.Agreements;

public class AgreementPdfSignerTests
{
    [Fact]
    public async Task Generate_Produces_SamePageCount_Pdf()
    {
        // Uses a minimal 2-page PDF produced inline via QuestPDF.
        var original = MakeTwoPagePdf();
        var signaturePath = MakePng();
        var outPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var signers = new List<Signer>
            {
                new() { Name = "Alice", TypedName = "Alice", SignedAtUtc = DateTime.UtcNow, SignatureImagePath = signaturePath },
            };

            var gen = new AgreementPdfSigner();
            await gen.GenerateSignedPdfAsync(original, signers, outPath, CancellationToken.None);

            // Page count is preserved.
            var pages = Conversion.GetPageCount(await File.ReadAllBytesAsync(outPath), password: null);
            Assert.Equal(2, pages);

            // The stamp actually landed: the signed last page carries more ink than the untouched last page.
            using var originalLast = await RenderLastPageAsync(original);
            using var signedLast = await RenderLastPageAsync(outPath);
            Assert.True(CountInk(signedLast) > CountInk(originalLast),
                "the signature stamp should add ink to the last page");
        }
        finally
        {
            File.Delete(original);
            File.Delete(outPath);
            File.Delete(signaturePath);
        }
    }

    private static string MakeTwoPagePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        Document.Create(c =>
        {
            c.Page(p => p.Content().Text("page1"));
            c.Page(p => p.Content().Text("page2"));
        }).GeneratePdf(path);
        return path;
    }

    private static string MakePng()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        using var bmp = new SKBitmap(200, 60);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var font = new SKFont { Size = 16 };
            canvas.DrawText("Alice", 10, 40, SKTextAlign.Left, font, paint);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
        return path;
    }

    private static async Task<SKBitmap> RenderLastPageAsync(string pdfPath)
    {
        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        SKBitmap? last = null;
        await foreach (var bmp in Conversion.ToImagesAsync(
                           pdfBytes, password: null, options: new RenderOptions(), cancellationToken: CancellationToken.None))
        {
            last?.Dispose();
            last = bmp;
        }
        return last ?? throw new InvalidOperationException("No pages rendered.");
    }

    private static int CountInk(SKBitmap bmp)
    {
        var ink = 0;
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Red < 250 || c.Green < 250 || c.Blue < 250) ink++;
        }
        return ink;
    }
}
