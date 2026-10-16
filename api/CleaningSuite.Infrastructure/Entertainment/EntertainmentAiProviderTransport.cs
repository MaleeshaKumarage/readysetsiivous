using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Entertainment;

public sealed class ProviderRequestFailure(HttpStatusCode? status, TimeSpan? retryAfter, bool malformed = false, string? providerMessage = null) : Exception
{
    public HttpStatusCode? Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public bool Malformed { get; } = malformed;
    public string? ProviderMessage { get; } = providerMessage;
}

public sealed class EntertainmentAiProviderTransport(IHttpClientFactory clients, IConfiguration config)
{
    public async Task<JsonDocument> SendAsync(ProviderKey key, string instruction, string input, object schema, CancellationToken ct)
    {
        var timeout = Math.Clamp(config.GetValue<int?>("Entertainment:Ai:ProviderTimeoutSeconds") ?? 12, 1, 30);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        try
        {
            using var request = key.Provider == "Gemini"
                ? GeminiRequest(key.Value, instruction, input, schema)
                : OpenAiRequest(key.Value, instruction, input, schema);
            using var response = await clients.CreateClient(key.Provider).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                var errorBody = await response.Content.ReadAsStringAsync(deadline.Token);
                throw new ProviderRequestFailure(response.StatusCode, retry, providerMessage: ExtractProviderMessage(errorBody, key.Value));
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
            var body = Encoding.UTF8.GetString(output.ToArray());
            try
            {
                using var envelope = JsonDocument.Parse(body);
                var text = key.Provider == "Gemini" ? GeminiText(envelope.RootElement) : OpenAiText(envelope.RootElement);
                return JsonDocument.Parse(text);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new ProviderRequestFailure(null, null, malformed: true);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProviderRequestFailure(null, null);
        }
        catch (HttpRequestException)
        {
            throw new ProviderRequestFailure(null, null);
        }
    }

    private HttpRequestMessage GeminiRequest(string apiKey, string instruction, string input, object schema)
    {
        var baseUrl = config["Entertainment:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com";
        var model = config["Entertainment:Gemini:Model"] ?? "gemini-flash-latest";
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        request.Headers.Add("X-goog-api-key", apiKey);
        request.Content = JsonContent(new { systemInstruction = new { parts = new[] { new { text = instruction } } }, contents = new[] { new { role = "user", parts = new[] { new { text = input } } } }, generationConfig = new { responseMimeType = "application/json", responseSchema = schema, maxOutputTokens = 2048 } });
        return request;
    }

    private HttpRequestMessage OpenAiRequest(string apiKey, string instruction, string input, object schema)
    {
        var model = config["Entertainment:OpenAI:Model"] ?? "gpt-4.1-mini";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent(new { model, instructions = instruction, input, text = new { format = new { type = "json_schema", name = "game_result", strict = true, schema = ToOpenAiSchema(JsonSerializer.SerializeToNode(schema)!) } }, max_output_tokens = 2048 });
        return request;
    }

    private static HttpContent JsonContent(object value) => new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static JsonNode ToOpenAiSchema(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Key == "type" && property.Value is JsonValue typeValue)
                {
                    var type = typeValue.GetValue<string>();
                    obj["type"] = type.ToLowerInvariant();
                }
                else if (property.Key == "properties" && property.Value is JsonObject properties)
                {
                    obj["additionalProperties"] = false;
                    foreach (var child in properties) ToOpenAiSchema(child.Value!);
                    if (obj["required"] is null) obj["required"] = new JsonArray(properties.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray());
                }
                else if (property.Value is not null) ToOpenAiSchema(property.Value);
            }
        }
        else if (node is JsonArray array)
            foreach (var item in array) if (item is not null) ToOpenAiSchema(item);
        return node;
    }

    private static string GeminiText(JsonElement root) => root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? throw new JsonException();

    private static string? ExtractProviderMessage(string body, string secret)
    {
        try
        {
            using var error = JsonDocument.Parse(body);
            if (!error.RootElement.TryGetProperty("error", out var detail) ||
                !detail.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
                return null;
            var value = message.GetString();
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
            value = string.Concat(value.Where(c => !char.IsControl(c))).Trim();
            return value.Length <= 240 ? value : value[..240];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string OpenAiText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String) return direct.GetString()!;
        foreach (var item in root.GetProperty("output").EnumerateArray())
            foreach (var content in item.GetProperty("content").EnumerateArray())
                if (content.TryGetProperty("type", out var type) && type.GetString() == "output_text") return content.GetProperty("text").GetString() ?? throw new JsonException();
        throw new JsonException();
    }
}
