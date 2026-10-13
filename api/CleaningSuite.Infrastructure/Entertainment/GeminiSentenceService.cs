using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Entertainment;

public class GeminiSentenceService : IGeminiSentenceService
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;

    public GeminiSentenceService(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _model = configuration["Entertainment:Gemini:Model"] ?? "gemini-flash-latest";
        _baseUrl = configuration["Entertainment:Gemini:BaseUrl"]
            ?? "https://generativelanguage.googleapis.com";
    }

    public async Task<GeminiSentence> GenerateAsync(
        string description,
        CefrLevel level,
        string apiKey,
        IReadOnlyList<string> previousSentences,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/v1beta/models/{_model}:generateContent";
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = BuildPrompt(description, level, previousSentences) } } },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                // Non-thinking: a plain flash response is much faster than the
                // default "thinking" mode, which adds seconds of latency.
                thinkingConfig = new { thinkingBudget = 0 },
            },
        };

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-goog-api-key", apiKey);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await _http.SendAsync(request, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                return Parse(json);
            }

            var status = (int)response.StatusCode;
            if (attempt >= maxAttempts || !IsTransient(status))
                throw new InvalidOperationException($"Gemini request failed with {status}.");

            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
        }
    }

    private static bool IsTransient(int status)
        => status is 429 or 500 or 502 or 503 or 504;

    public static string BuildPrompt(string description, CefrLevel level, IReadOnlyList<string>? previousSentences = null)
    {
        var previous = previousSentences is null || previousSentences.Count == 0
            ? string.Empty
            : "\nPrevious sentences already used (do NOT reuse these or any near-duplicate):\n"
                + string.Join("\n", previousSentences.Select(s => $"- \"{s}\""));

        return $"""
            Generate one short English sentence for a Finnish learner at CEFR level {level}.
            Topic or context: {description}

            Vary the sentence every time: use different grammar (statements, questions,
            negatives), different tenses, different sub-topics within the topic, and
            different lengths. Never repeat a previous sentence or a close variant.
            {previous}

            Return ONLY a JSON object with exactly these keys and no surrounding text:
            - "sentenceEn": the English sentence.
            - "sentenceFi": the Finnish translation of that sentence.
            - "baseForms": an array of STRINGS, each one the Finnish base/dictionary
              form of a content word in the Finnish translation, in word order.
              Do NOT include the English forms.
            """;
    }

    public static GeminiSentence Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()
            ?? throw new InvalidOperationException("Gemini returned no text.");

        using var inner = JsonDocument.Parse(text);
        var root = inner.RootElement;

        var sentenceEn = root.GetProperty("sentenceEn").GetString()
            ?? throw new InvalidOperationException("Gemini returned an empty English sentence.");
        var sentenceFi = root.TryGetProperty("sentenceFi", out var fi) ? (fi.GetString() ?? "") : "";

        var baseForms = new List<string>();
        if (root.TryGetProperty("baseForms", out var forms))
        {
            foreach (var item in forms.EnumerateArray())
            {
                // The model may return plain strings or { "en", "fi" } objects.
                // We want the Finnish base form either way.
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    baseForms.Add(item.GetString() ?? "");
                else if (item.ValueKind == System.Text.Json.JsonValueKind.Object
                    && item.TryGetProperty("fi", out var fiForm))
                    baseForms.Add(fiForm.GetString() ?? "");
            }
        }

        return new GeminiSentence(sentenceEn, sentenceFi, baseForms);
    }
}
