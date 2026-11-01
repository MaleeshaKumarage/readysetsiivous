using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Partners;
using Microsoft.Extensions.Configuration;
namespace CleaningSuite.Infrastructure.Partners;

public sealed class PartnerNotificationTransport(HttpClient http, IConfiguration config) : IPartnerNotificationTransport
{
    public async Task SendAsync(PartnerNotificationDelivery delivery, CancellationToken ct)
    {
        var key = config["Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new PartnerNotificationSendException("Partner notification email is not configured.", false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("Idempotency-Key", $"partner/{delivery.Id:N}");
        request.Content = JsonContent.Create(new { from = config["Resend:From"] ?? "ReadySetSiivous <noreply@readysetsiivous.fi>", to = new[] { delivery.To }, subject = delivery.Subject, html = delivery.Html });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode) return;
            var retry = response.Headers.RetryAfter?.Delta;
            if (!retry.HasValue && response.Headers.RetryAfter?.Date is { } date) retry = date - DateTimeOffset.UtcNow;
            var transient = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500;
            // Do not copy provider bodies: they may contain request details or credentials.
            throw new PartnerNotificationSendException(transient ? "Email provider temporarily unavailable." : "Email provider rejected the notification. Check sender configuration.", transient, retry);
        }
        catch (HttpRequestException) { throw new PartnerNotificationSendException("Email service could not be reached.", true); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PartnerNotificationSendException("Email acceptance is uncertain; retry uses the same delivery ID.", true); }
    }
}
