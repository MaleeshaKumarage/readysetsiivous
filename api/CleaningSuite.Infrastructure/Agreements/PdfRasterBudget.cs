using PDFtoImage;
using FluentValidation;

namespace CleaningSuite.Infrastructure.Agreements;

public static class PdfRasterBudget
{
    public static int Dpi => Limit("PDF_RASTER_DPI", 144);
    public static int EncodedBytes => Limit("PDF_RASTER_ENCODED_BYTES", 64 * 1024 * 1024);
    private static int Limit(string name, int ceiling) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
        ? Math.Clamp(value, 1, ceiling) : ceiling;
    // Inspect every page before allocating the first raster. Native inspection itself
    // must run in the resource-capped worker, never in the API process.
    public static IReadOnlyList<(int Width, int Height)> Inspect(byte[] pdf)
    {
        var count = Conversion.GetPageCount(pdf, password: null);
        if (count is < 1 or > 500) throw new ValidationException("PDF must contain between 1 and 500 pages.");
        var result = new List<(int, int)>(count);
        long aggregate = 0;
        for (var i = 0; i < count; i++)
        {
            var size = Conversion.GetPageSize(pdf, i, password: null);
            var width = size.Width * Dpi / 72d;
            var height = size.Height * Dpi / 72d;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || width > Limit("PDF_RASTER_MAX_DIMENSION", 4096) || height > Limit("PDF_RASTER_MAX_DIMENSION", 4096))
                throw new ValidationException("PDF page dimensions exceed supported limits.");
            var w = checked((int)Math.Ceiling(width)); var h = checked((int)Math.Ceiling(height));
            var pixels = checked((long)w * h);
            aggregate = checked(aggregate + pixels);
            if (pixels > Limit("PDF_RASTER_PAGE_PIXELS", 8_000_000) || aggregate > Limit("PDF_RASTER_TOTAL_PIXELS", 64_000_000)) throw new ValidationException("PDF raster budget exceeded.");
            result.Add((w, h));
        }
        return result;
    }
}

public sealed class AgreementPdfSigner(IPdfWorkerClient worker) : CleaningSuite.Application.Agreements.IAgreementDocumentGenerator
{
    public Task GenerateSignedPdfAsync(string originalPdfPath, IReadOnlyList<CleaningSuite.Domain.Agreements.Signer> signers,
        string outputPath, CancellationToken ct = default) => worker.SignAsync(originalPdfPath, signers, outputPath, ct);
}
