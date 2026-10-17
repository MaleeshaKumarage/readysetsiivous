using CleaningSuite.Application.Common;
using CleaningSuite.Domain.QualityCycle;
using FluentValidation;
using FluentValidation.Results;
using PDFtoImage;
using SkiaSharp;
using System.Diagnostics.CodeAnalysis;

namespace CleaningSuite.Infrastructure.Uploads;

public sealed class UploadContentValidator : IUploadContentValidator
{
    private const int MaxPdfBytes = 20 * 1024 * 1024;
    private const int MaxPdfPages = 500;
    private const int MaxServiceImageBytes = 5 * 1024 * 1024;
    private const int MaxServiceImageDimension = 4096;
    private const long MaxServiceImagePixels = 20_000_000;
    private const int MaxSignatureBytes = 2 * 1024 * 1024;
    private const int MaxSignatureWidth = 1200;
    private const int MaxSignatureHeight = 400;
    private const int MaxQualityPhotoBytes = 10 * 1024 * 1024;
    private const int MaxQualityPhotoCount = 10;
    private const long MaxQualityTotalBytes = 20 * 1024 * 1024;
    private const long MaxQualityPhotoPixels = 40_000_000;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public void ValidateAgreementPdf(ReadOnlyMemory<byte> content)
        => ValidatePdf(content, MaxPdfBytes, "Agreement PDF");

    public void ValidateGeneratedAgreementPdf(ReadOnlyMemory<byte> content)
        => ValidatePdf(content, 100 * 1024 * 1024, "Signed PDF");

    private static void ValidatePdf(ReadOnlyMemory<byte> content, int maxBytes, string label)
    {
        if (content.Length > maxBytes)
            throw new UploadPayloadTooLargeException($"{label} exceeds the supported size limit.");
        if (content.Length == 0)
            Invalid("Pdf", $"Choose a non-empty {label.ToLowerInvariant()} document.");

        try
        {
            var bytes = content.ToArray();
            if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                Invalid("Pdf", $"The {label.ToLowerInvariant()} is not a valid PDF document.");
            var pageCount = Conversion.GetPageCount(bytes, password: null);
            if (pageCount is < 1 or > MaxPdfPages)
                Invalid("Pdf", $"PDF documents must contain between 1 and {MaxPdfPages} pages.");
        }
        catch (ValidationException) { throw; }
        catch (Exception)
        {
            Invalid("Pdf", $"The {label.ToLowerInvariant()} is corrupt, encrypted, or unsupported.");
        }
    }

    public void ValidateSignaturePng(ReadOnlyMemory<byte> content)
    {
        if (content.Length > MaxSignatureBytes)
            throw new UploadPayloadTooLargeException("Signature image must be 2 MiB or smaller.");
        ValidateImageBytes(content.Span, "SignaturePng", MaxSignatureWidth, MaxSignatureHeight,
            MaxSignatureWidth * (long)MaxSignatureHeight, SKEncodedImageFormat.Png);
        if (content.Length < PngMagic.Length || !content.Span[..PngMagic.Length].SequenceEqual(PngMagic))
            Invalid("SignaturePng", "Signature must be a valid PNG image.");
    }

    public string ValidateServiceImage(ReadOnlyMemory<byte> content)
    {
        if (content.Length > MaxServiceImageBytes)
            throw new UploadPayloadTooLargeException("Service image must be 5 MiB or smaller.");
        var format = GetEncodedFormat(content.Span, "Image", ["image/jpeg", "image/png", "image/webp"]);
        ValidateImageBytes(content.Span, "Image", MaxServiceImageDimension,
            MaxServiceImageDimension, MaxServiceImagePixels, format.Format);
        return format.MimeType switch { "image/jpeg" => ".jpg", "image/png" => ".png", _ => ".webp" };
    }

    public int ValidateQualityCyclePhotos(IReadOnlyList<QualityCyclePhoto>? photos, IReadOnlyList<string>? photoUrls)
    {
        if (photoUrls is { Count: > 0 })
            Invalid("PhotoUrls", "Submit photos with their individual notes using the Photos field.");
        if (photos is null || photos.Count == 0) return 0;
        if (photos.Count > MaxQualityPhotoCount)
            throw new UploadPayloadTooLargeException($"A shift submission can include at most {MaxQualityPhotoCount} photos.");

        long totalBytes = 0;
        foreach (var photo in photos)
        {
            if (photo is null || string.IsNullOrWhiteSpace(photo.Note))
                Invalid("Photos", "Add a separate note for each photo.");
            if (photo.Note.Trim().Length > 500)
                Invalid("Photos", "Each photo note must be 500 characters or fewer.");

            var parsed = ParseDataUrl(photo.Url, "Photos", MaxQualityPhotoBytes,
                ["image/png", "image/jpeg", "image/webp", "image/bmp"]);
            totalBytes += parsed.Bytes.Length;
            if (totalBytes > MaxQualityTotalBytes)
                throw new UploadPayloadTooLargeException("Photos in one shift submission must total 20 MiB or less.");

            ValidateImageBytes(parsed.Bytes, "Photos", int.MaxValue, int.MaxValue,
                MaxQualityPhotoPixels, parsed.Format);
        }

        return checked((int)totalBytes);
    }

    private static (string MimeType, SKEncodedImageFormat Format) GetEncodedFormat(ReadOnlySpan<byte> bytes, string field, string[] supported)
    {
        foreach (var mime in supported)
        {
            if (HasMime(bytes, mime))
                return (mime, FormatForMime(mime));
        }
        Invalid(field, "Choose a JPEG, PNG or WebP image.");
        return default;
    }

    private static void ValidateImageBytes(ReadOnlySpan<byte> bytes, string field,
        int maxWidth, int maxHeight, long maxPixels, SKEncodedImageFormat expectedFormat)
    {
        try
        {
            if (bytes.Length == 0) Invalid(field, "The image is empty.");

            using var data = SKData.CreateCopy(bytes.ToArray());
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat != expectedFormat)
                Invalid(field, "The image content does not match a supported image format.");

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width <= 0 || height <= 0 || width > maxWidth || height > maxHeight || (long)width * height > maxPixels)
                Invalid(field, "The image dimensions exceed the supported limits.");

            using var bitmap = new SKBitmap(codec.Info);
            var decodeResult = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
            if (decodeResult != SKCodecResult.Success || bitmap.Width != width || bitmap.Height != height)
                Invalid(field, "The image is truncated, corrupt, or unsupported.");
        }
        catch (ValidationException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Invalid(field, "The image is corrupt or unsupported.");
        }
    }

    private static (byte[] Bytes, string MimeType, SKEncodedImageFormat Format) ParseDataUrl(
        string? value, string field, int maxBytes, string[] supported)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            Invalid(field, "Photos must be embedded PNG, JPEG, WebP or BMP data images.");
        }

        var comma = value.IndexOf(',');
        if (comma < 0) Invalid(field, "The photo data is malformed.");
        var header = value[..comma];
        var mime = header.Split(';', 2)[0][5..].ToLowerInvariant();
        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase) || !supported.Contains(mime, StringComparer.Ordinal))
            Invalid(field, "Choose a PNG, JPEG, WebP or BMP photo encoded as base64.");

        var encoded = value.AsSpan(comma + 1);
        var maxEncodedLength = ((maxBytes + 2L) / 3L * 4L) + 4;
        if (encoded.Length > maxEncodedLength)
            throw new UploadPayloadTooLargeException("Each photo must be 10 MiB or smaller.");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded.ToString()); }
        catch (FormatException) { Invalid(field, "The photo base64 data is malformed."); return default; }
        if (bytes.Length > maxBytes)
            throw new UploadPayloadTooLargeException("Each photo must be 10 MiB or smaller.");
        return (bytes, mime, FormatForMime(mime));
    }

    private static bool HasMime(ReadOnlySpan<byte> bytes, string mime) => mime switch
    {
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(PngMagic),
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
        "image/bmp" => bytes.Length >= 2 && bytes[..2].SequenceEqual("BM"u8),
        _ => false,
    };

    private static SKEncodedImageFormat FormatForMime(string mime) => mime switch
    {
        "image/png" => SKEncodedImageFormat.Png,
        "image/jpeg" => SKEncodedImageFormat.Jpeg,
        "image/webp" => SKEncodedImageFormat.Webp,
        "image/bmp" => SKEncodedImageFormat.Bmp,
        _ => SKEncodedImageFormat.Bmp,
    };

    [DoesNotReturn]
    private static void Invalid(string field, string message) =>
        throw new ValidationException([new ValidationFailure(field, message)]);
}
