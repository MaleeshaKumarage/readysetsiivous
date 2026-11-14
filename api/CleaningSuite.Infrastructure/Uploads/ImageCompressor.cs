using SkiaSharp;

namespace CleaningSuite.Infrastructure.Uploads;

/// <summary>
/// Re-encodes uploaded photos and signatures before they are written to disk. Photos are scaled so the longest
/// edge is at most <see cref="MaxEdge"/>, EXIF rotation is applied (re-encoding drops the EXIF tag, so the pixels
/// must already be upright), and the original format is kept: JPEG and WebP at a visually lossless-for-evidence
/// quality, PNG losslessly so signature transparency survives. If re-encoding would not make the file smaller and
/// nothing needed changing, the original bytes are returned unchanged.
/// </summary>
public static class ImageCompressor
{
    public const int MaxEdge = 2048;
    public const int JpegQuality = 82;
    public const int WebpQuality = 80;

    public static byte[] Compress(byte[] content, string extension)
    {
        if (content.Length == 0) return content;
        try
        {
            using var data = SKData.CreateCopy(content);
            using var codec = SKCodec.Create(data);
            if (codec is null) return content;

            var origin = codec.EncodedOrigin;
            var info = codec.Info;
            var rotated = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var srcW = rotated ? info.Height : info.Width;
            var srcH = rotated ? info.Width : info.Height;
            var scale = Math.Min(1.0, (double)MaxEdge / Math.Max(srcW, srcH));
            var outW = Math.Max(1, (int)Math.Round(srcW * scale));
            var outH = Math.Max(1, (int)Math.Round(srcH * scale));

            using var source = SKBitmap.Decode(codec);
            if (source is null) return content;

            var target = new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(target);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)scale);
            var orientation = OrientationMatrix(origin, info.Width, info.Height);
            canvas.Concat(ref orientation);
            using var paint = new SKPaint();
            canvas.DrawBitmap(source, 0, 0, paint);
            canvas.Flush();

            using var image = surface.Snapshot();
            var format = extension switch
            {
                ".jpg" => SKEncodedImageFormat.Jpeg,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png,
            };
            var quality = format switch
            {
                SKEncodedImageFormat.Jpeg => JpegQuality,
                SKEncodedImageFormat.Webp => WebpQuality,
                _ => 100,
            };
            using var encoded = image.Encode(format, quality);
            var result = encoded.ToArray();

            // Keep the original when nothing changed and the re-encode is not smaller.
            var changed = origin != SKEncodedOrigin.TopLeft || scale < 1.0;
            if (!changed && result.Length >= content.Length) return content;
            return result;
        }
        catch (Exception)
        {
            // Unreadable input is rejected by the upload validator before it reaches here; never fail a save on it.
            return content;
        }
    }

    /// <summary>Maps source pixels to upright pixels for an EXIF orientation (source dimensions w by h).</summary>
    internal static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}
