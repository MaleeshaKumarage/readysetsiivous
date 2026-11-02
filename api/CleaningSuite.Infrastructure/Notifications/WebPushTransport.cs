using System.Text.Json;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Notifications;
using WebPush;

namespace CleaningSuite.Infrastructure.Notifications;

public sealed class WebPushTransport(PushSettings settings) : IPushTransport, IDisposable
{
    // Private client prevents framework HTTP logging from exposing delivery credentials.
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    public async Task<PushSendResult> SendAsync(PushDevice device, PushMessage message, DateTime expiresUtc, CancellationToken ct = default)
    {
        if (!settings.Available || !PushValidation.Endpoint(device.Endpoint) || !PushValidation.Keys(new(device.P256dh, device.Auth))) return new(false);
        try
        {
            var client = new WebPushClient(http);
            using var request = client.GenerateRequestDetails(new PushSubscription(device.Endpoint, device.P256dh, device.Auth),
                JsonSerializer.Serialize(new { title = message.Title, body = message.Body, url = message.Url, tag = message.Tag }),
                new Dictionary<string, object> { ["vapidDetails"] = new VapidDetails(settings.Subject, settings.PublicKey, settings.PrivateKey), ["TTL"] = Math.Max(0, (int)(expiresUtc - DateTime.UtcNow).TotalSeconds) });
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            var delay = response.Headers.RetryAfter?.Delta;
            if (delay is null && response.Headers.RetryAfter?.Date is { } date) delay = date - DateTimeOffset.UtcNow;
            return new(response.IsSuccessStatusCode, status is 404 or 410, status == 429 || status >= 500, delay);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, Retryable: true); }
        catch (HttpRequestException) { return new(false, Retryable: true); }
        catch (ArgumentException) { return new(false); }
    }
    public void Dispose() => http.Dispose();
}
