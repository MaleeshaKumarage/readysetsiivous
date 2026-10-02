using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Email;

/// <summary>Sends email through the Resend HTTP API.</summary>
public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _from;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, IConfiguration config, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _apiKey = config["Resend:ApiKey"] ?? "";
        _from = config["Resend:From"] ?? "ReadySetSiivous <noreply@readysetsiivous.fi>";
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Resend API key not configured; skipping email to {To}", to);
            return;
        }

        var payload = new
        {
            from = _from,
            to = new[] { to },
            subject,
            html,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend send failed ({Status}): {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Email send failed: {response.StatusCode}");
        }
    }
}
