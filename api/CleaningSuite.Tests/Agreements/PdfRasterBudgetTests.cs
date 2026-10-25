using System.Text;
using CleaningSuite.Infrastructure.Agreements;
using FluentValidation;

namespace CleaningSuite.Tests.Agreements;

public class PdfRasterBudgetTests
{
    [Fact]
    public void Ordinary_pages_have_explicit_144_dpi_dimensions()
    {
        var sizes = PdfRasterBudget.Inspect(Pdf(2, "0 0 595 842"));
        Assert.Equal(2, sizes.Count);
        Assert.All(sizes, size => Assert.Equal((1190, 1684), size));
    }

    [Theory]
    [InlineData("0 0 10000000 842")]
    [InlineData("0 0 595 10000000")]
    public void Oversized_geometry_is_rejected_without_rasterizing(string bounds)
        => Assert.Throws<ValidationException>(() => PdfRasterBudget.Inspect(Pdf(1, bounds)));

    [Fact]
    public void Aggregate_budget_rejects_many_normal_pages()
        => Assert.Throws<ValidationException>(() => PdfRasterBudget.Inspect(Pdf(40, "0 0 595 842")));

    [Fact]
    public void Excessive_page_metadata_is_rejected()
        => Assert.Throws<ValidationException>(() => PdfRasterBudget.Inspect(Pdf(501, "0 0 595 842")));

    [Fact]
    public async Task Rejected_native_attempt_preserves_original_and_existing_signed_artifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "pdf-budget-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var original = Path.Combine(root, "original.pdf");
            var prior = Path.Combine(root, "prior.pdf");
            var attempt = Path.Combine(root, "attempt.pdf");
            var hostile = Pdf(1, "0 0 10000000 842");
            var historical = Pdf(2, "0 0 595 842");
            await File.WriteAllBytesAsync(original, hostile);
            await File.WriteAllBytesAsync(prior, historical);
            await Assert.ThrowsAsync<ValidationException>(() => new NativeAgreementPdfSigner().GenerateSignedPdfAsync(original, [], attempt));
            Assert.Equal(hostile, await File.ReadAllBytesAsync(original));
            Assert.Equal(historical, await File.ReadAllBytesAsync(prior));
            Assert.False(File.Exists(attempt));
        }
        finally { Directory.Delete(root, true); }
    }

    // Fully synthetic, complete xref PDF. No customer documents or native fixture tools.
    public static byte[] Pdf(int pages, string bounds)
    {
        using var stream = new MemoryStream();
        void Write(string value) { var bytes = Encoding.ASCII.GetBytes(value); stream.Write(bytes); }
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        void Object(int id, string body) { offsets.Add(stream.Position); Write($"{id} 0 obj\n{body}\nendobj\n"); }
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Count {pages} /Kids [ {string.Join(' ', Enumerable.Range(3, pages).Select(i => $"{i} 0 R"))} ] >>");
        for (var i = 0; i < pages; i++) Object(i + 3, $"<< /Type /Page /Parent 2 0 R /MediaBox [{bounds}] /Resources << >> >>");
        var xref = stream.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return stream.ToArray();
    }
}
