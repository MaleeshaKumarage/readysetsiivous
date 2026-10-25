using System.Net;
using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Entertainment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Entertainment;

public sealed class ProviderRequestFailure(
    HttpStatusCode? status,
    TimeSpan? retryAfter,
    bool malformed = false,
    string? providerMessage = null,
    string? providerCode = null,
    string? providerReason = null,
    GeminiFailureKind kind = GeminiFailureKind.Unknown) : Exception
{
    public HttpStatusCode? Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public bool Malformed { get; } = malformed;
    public string? ProviderMessage { get; } = providerMessage;
    public string? ProviderCode { get; } = providerCode;
    public string? ProviderReason { get; } = providerReason;
    public GeminiFailureKind Kind { get; } = kind;
}

/// <summary>Gemini-only game transport with bounded retries and response parsing.</summary>
public sealed class EntertainmentAiProviderTransport(IHttpClientFactory clients, IConfiguration config, ILogger<EntertainmentAiProviderTransport>? logger = null)
{
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(55);
    private const int MaxErrorBodyBytes = 16 * 1024;

    public async Task<JsonDocument> SendAsync(ProviderKey key, string instruction, string input, object schema, CancellationToken ct)
    {
        if (!string.Equals(key.Provider, "Gemini", StringComparison.Ordinal))
            throw new InvalidOperationException("The language game supports Gemini requests only.");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(RequestBudget);
        var started = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            budget.Token.ThrowIfCancellationRequested();
            try
            {
                return await SendOnceAsync(key, instruction, input, schema, budget.Token);
            }
            catch (ProviderRequestFailure failure) when (attempt == 0 && ShouldRetry(failure))
            {
                var wait = failure.RetryAfter ?? Backoff(attempt);
                if (!CanRetryWithinBudget(started, wait)) throw;
                logger?.LogInformation("Gemini returned {Kind} (HTTP {Status}); retrying once after {DelayMilliseconds} ms.", failure.Kind, (int?)failure.Status, (int)wait.TotalMilliseconds);
                await Task.Delay(wait, budget.Token);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                var wait = Backoff(attempt);
                if (!CanRetryWithinBudget(started, wait)) throw new ProviderRequestFailure(null, null, kind: GeminiFailureKind.ProviderUnavailable);
                await Task.Delay(wait, budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt == 0 && !budget.IsCancellationRequested)
            {
                await Task.Delay(Backoff(attempt), budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ProviderRequestFailure(null, null, kind: GeminiFailureKind.ProviderUnavailable);
            }
            catch (HttpRequestException)
            {
                throw new ProviderRequestFailure(null, null, kind: GeminiFailureKind.ProviderUnavailable);
            }
        }

        throw new ProviderRequestFailure(null, null, kind: GeminiFailureKind.ProviderUnavailable);
    }

    private async Task<JsonDocument> SendOnceAsync(ProviderKey key, string instruction, string input, object schema, CancellationToken ct)
    {
        var timeout = GetAttemptTimeout();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var baseUrl = config["Entertainment:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com";
        var model = config["Entertainment:Gemini:Model"] ?? "gemini-3.5-flash-lite";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        request.Headers.Add("X-goog-api-key", key.Value);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = instruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = input } } } },
            generationConfig = new { responseMimeType = "application/json", responseSchema = schema, maxOutputTokens = 2048 }
        }), Encoding.UTF8, "application/json");

        using var response = await clients.CreateClient("Gemini").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            var retry = GetRetryAfter(response);
            var body = await ReadBoundedAsync(response.Content, deadline.Token);
            var parsed = GeminiProviderErrorParser.Parse(response.StatusCode, body, key.Value);
            throw new ProviderRequestFailure(response.StatusCode, retry, providerMessage: parsed.Message,
                providerCode: parsed.Code, providerReason: parsed.Reason, kind: parsed.Kind);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await responseStream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (output.Length + read > 32768) throw new ProviderRequestFailure(null, null, malformed: true);
            await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
        }
        try
        {
            using var envelope = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
            var text = GeminiText(envelope.RootElement);
            return JsonDocument.Parse(text);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new ProviderRequestFailure(null, null, malformed: true);
        }
    }

    private static bool ShouldRetry(ProviderRequestFailure failure) =>
        failure.Kind is GeminiFailureKind.RateLimited or GeminiFailureKind.UnknownRateLimit or GeminiFailureKind.ProviderUnavailable;

    private TimeSpan GetAttemptTimeout() => TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("Entertainment:Ai:ProviderTimeoutSeconds") ?? 25, 1, 30));

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt) + Random.Shared.Next(100, 401));

    private bool CanRetryWithinBudget(DateTimeOffset started, TimeSpan wait) =>
        DateTimeOffset.UtcNow - started + wait + GetAttemptTimeout() + TimeSpan.FromSeconds(1) < RequestBudget;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var value = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
        if (value is null || value <= TimeSpan.Zero) return null;
        return TimeSpan.FromSeconds(Math.Min(value.Value.TotalSeconds, 86400));
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[2048];
        int read;
        while (output.Length < MaxErrorBodyBytes && (read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxErrorBodyBytes - (int)output.Length)), ct)) > 0)
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string GeminiText(JsonElement root) => root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? throw new JsonException();
}
