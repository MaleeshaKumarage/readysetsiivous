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
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/v1beta/models/{_model}:generateContent";
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = BuildPrompt(description, level) } } },
            },
            generationConfig = new { responseMimeType = "application/json" },
        };

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-goog-api-key", apiKey);

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
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

    public static string BuildPrompt(string description, CefrLevel level)
    {
        return $"""
            Generate one short English sentence for a Finnish learner at CEFR level {level}.
            Topic or context: {description}

            Return ONLY a JSON object with exactly these keys and no surrounding text:
            - "sentenceEn": the English sentence.
            - "sentenceFi": the Finnish translation of that sentence.
            - "baseForms": an array of the base/dictionary form of each content word
              in the English sentence, in the order the words appear.
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

        var sentence = JsonSerializer.Deserialize<GeminiSentence>(
            text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Gemini returned an invalid sentence payload.");

        if (string.IsNullOrWhiteSpace(sentence.SentenceEn))
            throw new InvalidOperationException("Gemini returned an empty English sentence.");

        return sentence;
    }
}
