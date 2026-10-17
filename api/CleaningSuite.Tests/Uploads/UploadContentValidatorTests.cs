using CleaningSuite.Application.Common;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.Uploads;
using FluentValidation;
using SkiaSharp;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Buffers.Binary;

namespace CleaningSuite.Tests.Uploads;

public class UploadContentValidatorTests
{
    private readonly UploadContentValidator _validator = new();

    [Fact]
    public void Signature_requires_a_decodable_png_within_dimensions()
    {
        var png = Image(SKEncodedImageFormat.Png, 1200, 400);
        _validator.ValidateSignaturePng(png);
        Assert.Throws<ValidationException>(() => _validator.ValidateSignaturePng(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.Throws<ValidationException>(() => _validator.ValidateSignaturePng(Image(SKEncodedImageFormat.Png, 1201, 400)));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    [InlineData(SKEncodedImageFormat.Bmp, "image/bmp")]
    public void Quality_cycle_accepts_supported_decodable_photos_with_notes(SKEncodedImageFormat format, string mime)
    {
        var value = DataUrl(mime, Image(format, 80, 60));
        var size = _validator.ValidateQualityCyclePhotos([new(value, "Cleaned corridor")], []);
        Assert.True(size > 0);
    }

    [Fact]
    public void Quality_cycle_rejects_fake_mime_malformed_data_and_legacy_photo_urls()
    {
        var png = Image(SKEncodedImageFormat.Png, 80, 60);
        Assert.Throws<ValidationException>(() => _validator.ValidateQualityCyclePhotos([new(DataUrl("image/jpeg", png), "Note")], []));
        Assert.Throws<ValidationException>(() => _validator.ValidateQualityCyclePhotos([new("https://example.test/photo.png", "Note")], []));
        Assert.Throws<ValidationException>(() => _validator.ValidateQualityCyclePhotos([new("data:image/png;base64,%%%", "Note")], []));
        Assert.Throws<ValidationException>(() => _validator.ValidateQualityCyclePhotos([new(DataUrl("image/png", png[..^8]), "Note")], []));
        Assert.Throws<ValidationException>(() => _validator.ValidateQualityCyclePhotos([], [" "]));
        Assert.Throws<ValidationException>(() => _validator.ValidateQualityCyclePhotos([new(DataUrl("image/png", png), " ")], []));
    }

    [Fact]
    public void Quality_cycle_enforces_photo_count_and_aggregate_bytes()
    {
        var photo = new QualityCyclePhoto(DataUrl("image/png", Image(SKEncodedImageFormat.Png, 10, 10)), "Note");
        Assert.Throws<UploadPayloadTooLargeException>(() => _validator.ValidateQualityCyclePhotos(Enumerable.Repeat(photo, 11).ToArray(), []));

        var encodedOverLimit = "data:image/png;base64," + new string('A', 28_000_000);
        Assert.Throws<UploadPayloadTooLargeException>(() => _validator.ValidateQualityCyclePhotos([new(encodedOverLimit, "Note")], []));
    }

    [Fact]
    public void Service_image_extension_comes_from_decoded_content_not_filename()
    {
        Assert.Equal(".png", _validator.ValidateServiceImage(Image(SKEncodedImageFormat.Png, 80, 60)));
        Assert.Throws<ValidationException>(() => _validator.ValidateServiceImage(Image(SKEncodedImageFormat.Bmp, 80, 60)));
    }

    [Fact]
    public void Agreement_pdf_rejects_corrupt_and_oversized_content()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var validPdf = Document.Create(c => c.Page(p => p.Content().Text("Valid agreement"))).GeneratePdf();
        _validator.ValidateAgreementPdf(validPdf);
        _validator.ValidateGeneratedAgreementPdf(validPdf);
        Assert.Throws<ValidationException>(() => _validator.ValidateAgreementPdf("not a pdf"u8.ToArray()));
        Assert.Throws<UploadPayloadTooLargeException>(() => _validator.ValidateAgreementPdf(new byte[20 * 1024 * 1024 + 1]));
    }

    private static byte[] Image(SKEncodedImageFormat format, int width, int height)
    {
        if (format == SKEncodedImageFormat.Bmp)
        {
            var bmp = new byte[58];
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 1);
            BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);
            BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(34), 4);
            bmp[54] = 0xFF;
            return bmp;
        }
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 85);
        return encoded!.ToArray();
    }

    private static string DataUrl(string mime, byte[] bytes) => $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
}
