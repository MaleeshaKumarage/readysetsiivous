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
        var left = 40f;
        var top = bitmap.Height - 140f; // reserved bottom band
        foreach (var signer in signers)
        {
            DrawSignerSlot(canvas, left, top, signer);
            left += 220f;
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawSignerSlot(SKCanvas canvas, float left, float top, Signer signer)
    {
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont { Size = 12 };
        canvas.DrawText($"{signer.TypedName ?? signer.Name}", left, top, SKTextAlign.Left, font, paint);
        canvas.DrawText(signer.SignedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", left, top + 18, SKTextAlign.Left, font, paint);

        if (signer.SignatureImagePath is { } path && File.Exists(path))
        {
            using var sig = SKBitmap.Decode(path);
            // Defensive: a corrupt/non-image file decodes to null. Draw name/date only.
            if (sig is null)
                return;
            var scale = Math.Min(1f, 160f / sig.Width);
            var dest = new SKRect(left, top + 26, left + sig.Width * scale, top + 26 + sig.Height * scale);
            canvas.DrawBitmap(sig, dest, SKSamplingOptions.Default, paint);
        }
    }
}
