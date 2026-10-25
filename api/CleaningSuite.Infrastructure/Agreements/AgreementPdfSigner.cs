using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using PDFtoImage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace CleaningSuite.Infrastructure.Agreements;

public class NativeAgreementPdfSigner : IAgreementDocumentGenerator
{
    // QuestPDF ships Lato in the output tree; the Linux container has no system fonts, so
    // SKTypeface.Default renders nothing there. Load Lato explicitly with a default fallback.
    private static readonly SKTypeface SignatureTypeface = LoadTypeface();

    private static SKTypeface LoadTypeface()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "LatoFont", "Lato-Regular.ttf"),
            Path.Combine(AppContext.BaseDirectory, "LatoFont", "Lato-Bold.ttf"),
        };
        foreach (var path in candidates)
        {
            try { if (File.Exists(path)) return SKTypeface.FromFile(path); }
            catch { /* try next */ }
        }
        return SKTypeface.Default;
    }

    public async Task GenerateSignedPdfAsync(
        string originalPdfPath, IReadOnlyList<Signer> signers, string outputPath, CancellationToken ct = default)
    {
        // Native entry point: only the isolated task process may call this in production.
        var pdfBytes = await File.ReadAllBytesAsync(originalPdfPath, ct);
        var dimensions = PdfRasterBudget.Inspect(pdfBytes);
        var pageImages = new List<string>();
        var job = Path.Combine(Path.GetTempPath(), "rasters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        try
        {
            long encodedBytes = 0;
            for (var page = 0; page < dimensions.Count; page++)
            {
                ct.ThrowIfCancellationRequested();
                var size = dimensions[page];
                using var bitmap = Conversion.ToImage(pdfBytes, page, password: null,
                    options: new RenderOptions(Dpi: PdfRasterBudget.Dpi, Width: size.Width, Height: size.Height));
                var png = EncodePng(bitmap);
                encodedBytes = checked(encodedBytes + png.LongLength);
                if (encodedBytes > PdfRasterBudget.EncodedBytes)
                    throw new FluentValidation.ValidationException("PDF encoded raster budget exceeded.");
                var pagePath = Path.Combine(job, $"page-{page}.png");
                await File.WriteAllBytesAsync(pagePath, png, ct);
                pageImages.Add(pagePath);
            }

            var last = await File.ReadAllBytesAsync(pageImages[^1], ct);
            var stamped = StampLastPage(last, signers);
            if (checked(encodedBytes - last.LongLength + stamped.LongLength) > PdfRasterBudget.EncodedBytes)
                throw new FluentValidation.ValidationException("Stamped PDF exceeds encoded raster budget.");
            await File.WriteAllBytesAsync(pageImages[^1], stamped, ct);

            QuestPDF.Settings.License = LicenseType.Community;
            using var output = new PdfOutputStream(new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None), 100L * 1024 * 1024);
            Document.Create(c =>
            {
                foreach (var imagePath in pageImages)
                {
                    c.Page(p =>
                    {
                        p.Size(PageSizes.A4);
                        p.Margin(0);
                        p.Content().Image(imagePath);
                    });
                }
            }).GeneratePdf(output);
        }
        finally { Directory.Delete(job, true); }
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] StampLastPage(byte[] pagePng, IReadOnlyList<Signer> signers)
    {
        using var bitmap = SKBitmap.Decode(pagePng);
        using var canvas = new SKCanvas(bitmap);
        const float margin = 60f;
        var top = bitmap.Height - 230f; // reserved bottom band
        var usable = bitmap.Width - 2 * margin;
        var columns = Math.Max(1, signers.Count);

        // Distribute signers symmetrically around the page center: each gets a column
        // and is centered within it, so two signers sit on either side of the middle.
        for (var i = 0; i < signers.Count; i++)
        {
            var centerX = margin + (usable / columns) * (i + 0.5f);
            DrawSignerSlot(canvas, centerX, top, signers[i]);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawSignerSlot(SKCanvas canvas, float centerX, float top, Signer signer)
    {
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var nameFont = new SKFont(SignatureTypeface, 28);
        using var dateFont = new SKFont(SignatureTypeface, 18);
        using var datePaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };

        float cursorY = top;
        const float targetWidth = 240f;
        const float maxHeight = 120f;

        // Signature image on top, centered, scaled up and trimmed of padding.
        if (signer.SignatureImagePath is { } path && File.Exists(path))
        {
            using var raw = SKBitmap.Decode(path);
            // Defensive: a corrupt/non-image file decodes to null. Fall through to name only.
            if (raw is not null)
            {
                using var sig = TrimTransparent(raw);
                if (sig is not null)
                {
                    var scale = Math.Min(targetWidth / sig.Width, maxHeight / sig.Height);
                    var w = sig.Width * scale;
                    var h = sig.Height * scale;
                    using var sigPaint = new SKPaint { IsAntialias = true };
                    canvas.DrawBitmap(sig, new SKRect(centerX - w / 2, cursorY, centerX + w / 2, cursorY + h), SKSamplingOptions.Default, sigPaint);
                    cursorY += h + 12f;
                }
            }
        }

        // Name directly under the signature, centered.
        canvas.DrawText($"{signer.TypedName ?? signer.Name}", centerX, cursorY + 30, SKTextAlign.Center, nameFont, textPaint);
        // Date under the name, centered.
        var date = signer.SignedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
        canvas.DrawText(date, centerX, cursorY + 56, SKTextAlign.Center, dateFont, datePaint);
    }

    /// <summary>
    /// Crop transparent borders so the signature scales to fill its slot rather than
    /// retaining the blank canvas padding around the strokes.
    /// </summary>
    private static SKBitmap? TrimTransparent(SKBitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).Alpha == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0) return null; // fully transparent

        var rect = new SKRectI(minX, minY, maxX + 1, maxY + 1);
        var cropped = new SKBitmap(rect.Width, rect.Height);
        return bmp.ExtractSubset(cropped, rect) ? cropped : bmp;
    }
}
