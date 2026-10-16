using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Entertainment;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Entertainment;

/// <summary>One process-wide provider lane, including rate-limit cooldown, for generation and grading.</summary>
public sealed class GeminiProviderTransport(HttpClient http, IConfiguration configuration)
{
    private static readonly SemaphoreSlim Lane = new(1, 1);
    private static DateTimeOffset _availableAt;
    public async Task<JsonDocument> SendAsync(string instruction, string data, object schema, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = configuration["Entertainment:Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new EntertainmentAiUnavailableException();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(55));
        var entered = false;
        try
        {
            await Lane.WaitAsync(budget.Token); entered = true;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var wait = _availableAt - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, budget.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>("Entertainment:Gemini:TimeoutSeconds") ?? 25, 1, 25)));
                var baseUrl = configuration["Entertainment:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com";
                var model = configuration["Entertainment:Gemini:Model"] ?? "gemini-flash-latest";
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
                request.Headers.Add("X-goog-api-key", key);
                request.Content = new StringContent(JsonSerializer.Serialize(new {
                    systemInstruction = new { parts = new[] { new { text = instruction } } },
                    contents = new[] { new { role = "user", parts = new[] { new { text = data } } } },
                    generationConfig = new { responseMimeType = "application/json", responseSchema = schema, maxOutputTokens = 2048 }
                }), Encoding.UTF8, "application/json");
                try
                {
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        var transient = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                        if (!transient) throw new EntertainmentAiUnavailableException();
                        var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                        _availableAt = DateTimeOffset.UtcNow + (retry > TimeSpan.Zero ? retry.Value : TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt) + Random.Shared.Next(100, 300)));
                        if (attempt == 0) continue;
                        throw new EntertainmentAiUnavailableException();
                    }
                    await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                    using var output = new MemoryStream();
                    var buffer = new byte[4096];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0) {
                        if (output.Length + read > 32768) throw new EntertainmentAiUnavailableException();
                        output.Write(buffer, 0, read);
                    }
                    ct.ThrowIfCancellationRequested();
                    return ParseEnvelope(Encoding.UTF8.GetString(output.ToArray()));
                }
                catch (HttpRequestException) when (attempt == 0) { _availableAt = DateTimeOffset.UtcNow.AddMilliseconds(600 + Random.Shared.Next(100, 300)); }
                catch (OperationCanceledException) when (!budget.IsCancellationRequested && attempt == 0) { }
            }
            throw new EntertainmentAiUnavailableException();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new EntertainmentAiUnavailableException(); }
        catch (HttpRequestException) { throw new EntertainmentAiUnavailableException(); }
        catch (JsonException) { throw new EntertainmentAiUnavailableException(); }
        catch (KeyNotFoundException) { throw new EntertainmentAiUnavailableException(); }
        catch (InvalidOperationException) { throw new EntertainmentAiUnavailableException(); }
        catch (IndexOutOfRangeException) { throw new EntertainmentAiUnavailableException(); }
        finally { if (entered) Lane.Release(); }
    }
    public static JsonDocument ParseEnvelope(string json)
    {
        using var envelope = JsonDocument.Parse(json);
        var text = envelope.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        return JsonDocument.Parse(text ?? throw new EntertainmentAiUnavailableException());
    }
    internal static string Text(JsonElement root, string property, int maximum)
    {
        var text = root.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(c => char.IsControl(c) && c != '\n')) throw new EntertainmentAiUnavailableException();
        return text;
    }
    internal static object StringSchema => new { type = "STRING" };
}
