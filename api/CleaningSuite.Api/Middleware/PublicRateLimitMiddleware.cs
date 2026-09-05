using System.Collections.Concurrent;

namespace CleaningSuite.Api.Middleware;

/// <summary>
/// Sliding-window rate limit for anonymous public endpoints (booking abuse guard).
/// MVP: in-memory, per IP, 30 requests per minute. Single instance; a
/// distributed limiter comes if the API ever scales out.
/// </summary>
public class PublicRateLimitMiddleware
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaxRequests = 30;

    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();
    private readonly RequestDelegate _next;

    public PublicRateLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.Contains("/api/v1/public/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var queue = _hits.GetOrAdd(ip, _ => new Queue<DateTime>());
        var now = DateTime.UtcNow;

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > Window)
                queue.Dequeue();

            if (queue.Count >= MaxRequests)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }

            queue.Enqueue(now);
        }

        await _next(context);
    }
}
