using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using PDFtoImage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace CleaningSuite.Infrastructure.Agreements;

public class AgreementPdfSigner : IAgreementDocumentGenerator
{
    public async Task GenerateSignedPdfAsync(
        string originalPdfPath, IReadOnlyList<Signer> signers, string outputPath, CancellationToken ct = default)
    {
        // Render every page to a PNG (PDFtoImage 5.4.0 streams SKBitmap pages via ToImagesAsync).
        // Note: the string overloads expect Base64 content, so pass the raw PDF bytes.
        var pdfBytes = await File.ReadAllBytesAsync(originalPdfPath, ct);
        var pageImages = new List<byte[]>();
        await foreach (var bitmap in Conversion.ToImagesAsync(
                           pdfBytes, password: null, options: new RenderOptions(), cancellationToken: ct))
        {
            pageImages.Add(EncodePng(bitmap));
            bitmap.Dispose();
        }

        // Stamp the signature block on the last page.
        pageImages[^1] = StampLastPage(pageImages[^1], signers);

        // Rebuild the PDF from images (one full-bleed page each).
        QuestPDF.Settings.License = LicenseType.Community;
        Document.Create(c =>
        {
            foreach (var img in pageImages)
            {
                c.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.Margin(0);
                    p.Content().Image(img);
                });
            }
        }).GeneratePdf(outputPath);
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
        const float slotWidth = 220f;
        var left = 40f;
        var top = bitmap.Height - 160f; // reserved bottom band
        foreach (var signer in signers)
        {
            DrawSignerSlot(canvas, left, top, signer);
            left += slotWidth;
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawSignerSlot(SKCanvas canvas, float left, float top, Signer signer)
    {
        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var nameFont = new SKFont { Size = 13 };
        using var dateFont = new SKFont { Size = 11 };
        using var datePaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };

        float cursorY = top;
        const float targetWidth = 190f;
        const float maxHeight = 100f;

        // Signature image on top, scaled up and trimmed of transparent padding.
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
                    canvas.DrawBitmap(sig, new SKRect(left, cursorY, left + w, cursorY + h), SKSamplingOptions.Default, sigPaint);
                    cursorY += h + 8f;
                }
            }
        }

        // Name directly under the signature.
        canvas.DrawText($"{signer.TypedName ?? signer.Name}", left, cursorY + 14, SKTextAlign.Left, nameFont, textPaint);
        // Date under the name.
        var date = signer.SignedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
        canvas.DrawText(date, left, cursorY + 30, SKTextAlign.Left, dateFont, datePaint);
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
