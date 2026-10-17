namespace CleaningSuite.Application.Common;

public static class BoundedStreamReader
{
    public static async Task<byte[]> ReadAsync(Stream input, int maximumBytes, string field, CancellationToken ct)
    {
        if (input is null) throw new FluentValidation.ValidationException($"{field} is required.");
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (input.CanSeek && input.Length > maximumBytes)
            throw new UploadPayloadTooLargeException($"{field} must be {maximumBytes / (1024 * 1024)} MiB or smaller.");

        await using var output = new MemoryStream(Math.Min(maximumBytes, input.CanSeek ? (int)input.Length : 81920));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
                throw new UploadPayloadTooLargeException($"{field} must be {maximumBytes / (1024 * 1024)} MiB or smaller.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }
}
