using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleaningSuite.Infrastructure.Entertainment;

public class GeminiSentenceService : IGeminiSentenceService
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly ILogger<GeminiSentenceService> _logger;

    public GeminiSentenceService(HttpClient http, IConfiguration configuration, ILogger<GeminiSentenceService>? logger = null)
    {
        _http = http;
        _model = configuration["Entertainment:Gemini:Model"] ?? "gemini-flash-latest";
        _baseUrl = configuration["Entertainment:Gemini:BaseUrl"]
            ?? "https://generativelanguage.googleapis.com";
        _timeout = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>("Entertainment:Gemini:TimeoutSeconds") ?? 8, 1, 30));
        _logger = logger ?? NullLogger<GeminiSentenceService>.Instance;
    }

    public async Task<GeminiSentence> GenerateAsync(
        string description,
        CefrLevel level,
        string apiKey,
        IReadOnlyList<string> previousSentences,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentNullException.ThrowIfNull(previousSentences);
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

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-goog-api-key", apiKey);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            using var response = await _http.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
                return Fallback(level, previousSentences, ct, $"HTTP {(int)response.StatusCode}");
            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var parsed = ParseValidResponse(json);
            if (parsed is null) return Fallback(level, previousSentences, ct, "invalid response");
            ct.ThrowIfCancellationRequested();
            return parsed;
        }
        catch (HttpRequestException) { return Fallback(level, previousSentences, ct, "network failure"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fallback(level, previousSentences, ct, "provider timeout"); }
    }

    private GeminiSentence Fallback(CefrLevel level, IReadOnlyList<string> previousSentences, CancellationToken ct, string reason)
    {
        ct.ThrowIfCancellationRequested();
        var sample = SampleSentenceCatalog.Select(level, previousSentences);
        _logger.LogWarning("Gemini unavailable ({Reason}); using a sample sentence for {Level}.", reason, level);
        ct.ThrowIfCancellationRequested();
        return sample;
    }

    private static GeminiSentence? ParseValidResponse(string json)
    {
        try
        {
            var sentence = Parse(json);
            if (string.IsNullOrWhiteSpace(sentence.SentenceEn) || string.IsNullOrWhiteSpace(sentence.SentenceFi)
                || sentence.BaseForms.Count == 0 || sentence.BaseForms.Any(string.IsNullOrWhiteSpace)) return null;
            return sentence;
        }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

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
                else throw new InvalidOperationException("Gemini returned an unsupported word-hint format.");
            }
        }

        return new GeminiSentence(sentenceEn, sentenceFi, baseForms);
    }
}
