using CleaningSuite.Infrastructure.Uploads;
using SkiaSharp;
using Xunit;

namespace CleaningSuite.Tests.Uploads;

public sealed class ImageCompressorTests
{
    private static byte[] NoiseImage(int width, int height, SKEncodedImageFormat format, int quality)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var random = new Random(7);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, quality);
        return encoded.ToArray();
    }

    private static SKBitmap Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return SKBitmap.Decode(codec)!;
    }

    [Fact]
    public void Large_jpeg_is_scaled_to_the_longest_edge_limit_and_made_smaller()
    {
        var original = NoiseImage(3000, 2000, SKEncodedImageFormat.Jpeg, 100);

        var compressed = ImageCompressor.Compress(original, ".jpg");

        Assert.True(compressed.Length < original.Length);
        using var decoded = Decode(compressed);
        Assert.Equal(ImageCompressor.MaxEdge, decoded.Width);
        Assert.Equal(1365, decoded.Height);
        using var data = SKData.CreateCopy(compressed);
        using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec!.EncodedFormat);
    }

    [Fact]
    public void Small_image_is_never_made_bigger()
    {
        var original = NoiseImage(120, 80, SKEncodedImageFormat.Jpeg, 30);

        var compressed = ImageCompressor.Compress(original, ".jpg");

        Assert.True(compressed.Length <= original.Length);
    }

    [Fact]
    public void Png_keeps_transparency_for_signatures()
    {
        using var bitmap = new SKBitmap(64, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        for (var x = 0; x < 32; x++)
            bitmap.SetPixel(x, 16, new SKColor(0, 0, 0, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var original = encoded.ToArray();

        var compressed = ImageCompressor.Compress(original, ".png");

        using var decoded = Decode(compressed);
        Assert.Equal(0, decoded.GetPixel(60, 0).Alpha);
        Assert.Equal(255, decoded.GetPixel(10, 16).Alpha);
    }

    [Fact]
    public void Unreadable_bytes_are_returned_unchanged()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5 };

        Assert.Equal(garbage, ImageCompressor.Compress(garbage, ".jpg"));
    }

    [Fact]
    public void Empty_input_is_returned_unchanged()
    {
        Assert.Empty(ImageCompressor.Compress([], ".jpg"));
    }
}
