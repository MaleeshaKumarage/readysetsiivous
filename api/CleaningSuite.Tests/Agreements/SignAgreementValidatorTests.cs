using CleaningSuite.Application.Agreements.Commands;

namespace CleaningSuite.Tests.Agreements;

public class SignAgreementValidatorTests
{
    private readonly SignAgreementValidator _validator = new();

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Valid_Png_Passes()
    {
        var cmd = new SignAgreementCommand("tok", "Alice", MakePng(100, 50));
        Assert.True(_validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void NonPng_MagicBytes_AreRejected()
    {
        var cmd = new SignAgreementCommand("tok", "Alice", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        var errors = _validator.Validate(cmd).Errors;
        Assert.Contains(errors, e => e.PropertyName == nameof(SignAgreementCommand.SignaturePng));
    }

    [Fact]
    public void Oversized_Signature_IsRejected()
    {
        var png = MakePng(100, 50);
        var oversized = new byte[2 * 1024 * 1024 + 1];
        png.CopyTo(oversized, 0);
        var cmd = new SignAgreementCommand("tok", "Alice", oversized);
        var errors = _validator.Validate(cmd).Errors;
        Assert.Contains(errors, e => e.PropertyName == nameof(SignAgreementCommand.SignaturePng));
    }

    [Fact]
    public void Oversized_Dimensions_AreRejected()
    {
        // Valid magic bytes but an IHDR declaring an enormous width (decompression bomb).
        var cmd = new SignAgreementCommand("tok", "Alice", MakePng(5000, 50));
        var errors = _validator.Validate(cmd).Errors;
        Assert.Contains(errors, e => e.PropertyName == nameof(SignAgreementCommand.SignaturePng));
    }

    private static byte[] MakePng(int width, int height)
    {
        var png = new byte[24];
        PngMagic.CopyTo(png, 0);
        // IHDR: length (4) + "IHDR" (4), then width (16-19) and height (20-23) big-endian.
        png[16] = (byte)(width >> 24);
        png[17] = (byte)(width >> 16);
        png[18] = (byte)(width >> 8);
        png[19] = (byte)width;
        png[20] = (byte)(height >> 24);
        png[21] = (byte)(height >> 16);
        png[22] = (byte)(height >> 8);
        png[23] = (byte)height;
        return png;
    }
}
