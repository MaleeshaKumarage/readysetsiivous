using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;

namespace CleaningSuite.Api.Middleware;

public sealed class PublicRateLimitOptions
{
    public int Capacity { get; set; } = 10000;
}

/// <summary>Single-instance, bounded, per-client 30 request / 60 second sliding window.</summary>
public sealed class PublicRateLimitMiddleware
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaxRequests = 30;
    private readonly object _sync = new();
    private readonly Dictionary<string, Queue<long>> _hits = new(StringComparer.Ordinal);
    private readonly RequestDelegate _next;
    private readonly TimeProvider _time;
    private readonly int _capacity;

    public PublicRateLimitMiddleware(RequestDelegate next, TimeProvider time, IOptions<PublicRateLimitOptions> options)
    {
        _next = next;
        _time = time;
        _capacity = options.Value.Capacity;
        if (_capacity is < 1 or > 100000)
            throw new InvalidOperationException("PublicRateLimit:Capacity must be between 1 and 100000.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.Contains("/api/v1/public/", StringComparison.OrdinalIgnoreCase)
            && !(path.Equals("/api/v1/entertainment/groups", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/v1/entertainment/groups/", StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var address = context.Connection.RemoteIpAddress;
        var ip = (address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address)?.ToString() ?? "unknown";
        TimeSpan? retry = null;
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            if (!_hits.TryGetValue(ip, out var queue))
            {
                if (_hits.Count >= _capacity)
                {
                    foreach (var key in _hits.Where(pair =>
                        _time.GetElapsedTime(pair.Value.Last(), now) >= Window).Select(pair => pair.Key).ToArray())
                        _hits.Remove(key);
                }
                if (_hits.Count >= _capacity)
                {
                    // No active identity loses its quota when new clients saturate the registry.
                    retry = _hits.Values.Min(q => Window - _time.GetElapsedTime(q.Last(), now));
                }
                else
                {
                    queue = new Queue<long>();
                    _hits.Add(ip, queue);
                }
            }
            if (retry is null)
            {
                while (queue!.Count > 0 && _time.GetElapsedTime(queue.Peek(), now) >= Window)
                    queue.Dequeue();
                if (queue.Count >= MaxRequests)
                    retry = Window - _time.GetElapsedTime(queue.Peek(), now);
                else
                    queue.Enqueue(now);
            }
        }
        if (retry is not null)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.Value.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            context.Response.Headers.CacheControl = "no-store";
            return;
        }
        await _next(context);
    }
}
