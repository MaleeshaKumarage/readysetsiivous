using System.Text.Json;
using System.Diagnostics;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Marten;
using CleaningSuite.Infrastructure.Persistence;

namespace CleaningSuite.Infrastructure.Entertainment;

public sealed class EntertainmentAiCoordinator(EntertainmentAiProviderTransport transport, IConfiguration config, ILogger<EntertainmentAiCoordinator> logger, IDocumentStore? store = null)
{
    private readonly object _sync = new();
    private readonly Dictionary<(string Provider, string Slot), DateTimeOffset> _cooldowns = [];
    private readonly Dictionary<(string Provider, string Model), DateTimeOffset> _providerCooldowns = [];
    private readonly HashSet<(string Provider, string Slot)> _disabled = [];
    private readonly Dictionary<string, int> _cursor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DateTimeOffset>> _localUsage = new(StringComparer.Ordinal);

    public Task<GeminiSentence> GenerateAsync(string topic, CefrLevel level, IReadOnlyList<string> previous, CancellationToken ct)
    {
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        var input = GeminiSentenceService.BuildPrompt(topic, level, previous);
        return ExecuteAsync("generate", GeminiSentenceService.Instruction, input, GeminiSentenceService.Schema,
            doc => GeminiSentenceService.ParseSentence(doc.RootElement), ct);
    }

    public Task<EntertainmentAnswerEvaluation> EvaluateAsync(string sentenceEn, string referenceFi, string answer, CefrLevel level, CancellationToken ct)
    {
        if (sentenceEn.Length > 1000 || referenceFi.Length > 1000 || answer.Length > 4000) throw new EntertainmentAiUnavailableException();
        var input = JsonSerializer.Serialize(new { sentenceEn, referenceFi, answer, level = level.ToString() });
        return ExecuteAsync("evaluate", GeminiAnswerEvaluator.Instruction, input, GeminiAnswerEvaluator.Schema,
            doc => GeminiAnswerEvaluator.ParseEvaluation(doc.RootElement, answer), ct);
    }

    private async Task<T> ExecuteAsync<T>(string operation, string instruction, string input, object schema, Func<JsonDocument, T> parse, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var providers = new[] { EntertainmentAiKeyPool.Gemini(config), EntertainmentAiKeyPool.OpenAi(config) };
        var diagnostics = new List<string>();
        if (providers.All(pool => pool.Count == 0))
        {
            logger.LogWarning("Game AI unavailable: no provider credentials are configured on the server.");
            throw new EntertainmentAiUnavailableException(diagnostic: "No Gemini or OpenAI API keys are configured on the server.");
        }
        foreach (var pool in providers)
        {
            var keys = Rotate(pool);
            if (pool.Count > 0 && keys.Count == 0)
            {
                diagnostics.Add(GetUnavailablePoolDiagnostic(pool));
                continue;
            }
            foreach (var key in keys)
            {
                ct.ThrowIfCancellationRequested();
                if (!await ReserveProviderRequestAsync(key, ct))
                {
                    diagnostics.Add($"{key.Provider} model {GetModel(key.Provider)}: the configured rolling 24-hour request budget is exhausted.");
                    logger.LogWarning("Game AI {Operation} skipped {Provider} model {Model}: rolling 24-hour request budget exhausted.", operation, key.Provider, GetModel(key.Provider));
                    break;
                }
                var elapsed = Stopwatch.StartNew();
                try
                {
                    using var result = await transport.SendAsync(key, instruction, input, schema, ct);
                    var parsed = parse(result);
                    MarkHealthy(key);
                    logger.LogInformation("Game AI {Operation} succeeded with {Provider} slot {Slot} in {ElapsedMilliseconds} ms.", operation, key.Provider, key.Slot, elapsed.ElapsedMilliseconds);
                    return parsed;
                }
                catch (ProviderRequestFailure failure)
                {
                    var status = (int?)failure.Status;
                    var transient = status is null or 408 or 429 or >= 500;
                    var stopProvider = false;
                    var cooldownSeconds = 0;
                    var disabled = false;
                    if (status is 401 or 403) { Disable(key); disabled = true; }
                    else if (failure.Malformed) { Cooldown(key, TimeSpan.FromSeconds(2)); cooldownSeconds = 2; }
                    else if (status == 429) { var cooldown = failure.RetryAfter ?? TimeSpan.FromMinutes(1); CooldownProvider(key, cooldown); stopProvider = true; cooldownSeconds = (int)Math.Clamp(cooldown.TotalSeconds, 1, 86400); }
                    else if (transient) { var cooldown = failure.RetryAfter ?? TimeSpan.FromSeconds(5); Cooldown(key, cooldown); cooldownSeconds = (int)Math.Clamp(cooldown.TotalSeconds, 1, 60); }
                    else if (status is >= 400 and < 500) stopProvider = true;
                    diagnostics.Add(FormatFailure(key, status, failure, disabled, cooldownSeconds));
                    logger.LogWarning("Game AI {Operation} failed with {Provider} slot {Slot}, category {Category}, status {Status}, cooldown {CooldownSeconds}s, disabled {Disabled}, elapsed {ElapsedMilliseconds} ms; advancing to another key/provider.", operation, key.Provider, key.Slot, failure.Malformed ? "malformed-response" : transient ? "transient" : "request-rejected", status, cooldownSeconds, disabled, elapsed.ElapsedMilliseconds);
                    if (stopProvider) break;
                }
                catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
                {
                    Cooldown(key, TimeSpan.FromSeconds(2));
                    diagnostics.Add($"{key.Provider} key slot {key.Slot}: the provider returned an invalid structured response.");
                    logger.LogWarning("Game AI {Operation} returned a structurally invalid result from {Provider} slot {Slot} in {ElapsedMilliseconds} ms; advancing to another key/provider.", operation, key.Provider, key.Slot, elapsed.ElapsedMilliseconds);
                }
                catch (EntertainmentAiUnavailableException)
                {
                    Cooldown(key, TimeSpan.FromSeconds(2));
                    diagnostics.Add($"{key.Provider} key slot {key.Slot}: the provider response did not pass game validation.");
                    logger.LogWarning("Game AI {Operation} returned an invalid game result from {Provider} slot {Slot} in {ElapsedMilliseconds} ms; advancing to another key/provider.", operation, key.Provider, key.Slot, elapsed.ElapsedMilliseconds);
                }
            }
        }
        var diagnostic = string.Join(Environment.NewLine, diagnostics.Distinct(StringComparer.Ordinal));
        if (diagnostic.Length > 2400) diagnostic = diagnostic[..2400] + "…";
        throw new EntertainmentAiUnavailableException(diagnostic: string.IsNullOrWhiteSpace(diagnostic) ? "No configured provider key is currently available. Retry shortly." : diagnostic);
    }

    private IReadOnlyList<ProviderKey> Rotate(IReadOnlyList<ProviderKey> pool)
    {
        if (pool.Count == 0) return [];
        lock (_sync)
        {
            var provider = pool[0].Provider;
            var start = _cursor.GetValueOrDefault(provider) % pool.Count;
            _cursor[provider] = (start + 1) % pool.Count;
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in _cooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) _cooldowns.Remove(expired);
            foreach (var expired in _providerCooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) _providerCooldowns.Remove(expired);
            return Enumerable.Range(0, pool.Count).Select(i => pool[(start + i) % pool.Count])
                .Where(k => !_disabled.Contains((k.Provider, k.Slot)) && !_cooldowns.ContainsKey((k.Provider, k.Slot)) && !_providerCooldowns.ContainsKey((k.Provider, GetModel(k.Provider)))).ToArray();
        }
    }

    private void Cooldown(ProviderKey key, TimeSpan duration) { lock (_sync) _cooldowns[(key.Provider, key.Slot)] = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 1, 60)); }
    private void CooldownProvider(ProviderKey key, TimeSpan duration) { lock (_sync) _providerCooldowns[(key.Provider, GetModel(key.Provider))] = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 1, 86400)); }
    private void Disable(ProviderKey key) { lock (_sync) _disabled.Add((key.Provider, key.Slot)); }
    private void MarkHealthy(ProviderKey key) { lock (_sync) _cooldowns.Remove((key.Provider, key.Slot)); }

    private int GetLimit(string provider, string limit, int fallback, int maximum) =>
        Math.Clamp(config.GetValue<int?>($"Entertainment:{provider}:RateLimits:{limit}") ?? fallback, 1, maximum);

    private string GetModel(string provider) => provider == "Gemini"
        ? config["Entertainment:Gemini:Model"] ?? "gemini-3.5-flash-lite"
        : config["Entertainment:OpenAI:Model"] ?? "gpt-4.1-mini";

    private async Task<bool> ReserveProviderRequestAsync(ProviderKey key, CancellationToken ct)
    {
        var requestsPerMinute = GetLimit(key.Provider, "RequestsPerMinute", key.Provider == "Gemini" ? 3 : 5, 1000);
        var requestsPerDay = GetLimit(key.Provider, "RequestsPerDay", key.Provider == "Gemini" ? 15 : 50, 100000);
        var minimumInterval = TimeSpan.FromSeconds(60d / requestsPerMinute);
        var usageId = $"{key.Provider}:{GetModel(key.Provider)}";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? waitUntil;
            if (store is not null)
            {
                await using var session = store.LightweightSession(TenantIds.Entertainment);
                var usage = await session.LoadAsync<EntertainmentAiUsageWindow>(usageId, ct) ?? new EntertainmentAiUsageWindow { Id = usageId };
                usage.RequestUtcTimestamps = usage.RequestUtcTimestamps.Where(time => time > now.AddHours(-24)).ToList();
                if (usage.RequestUtcTimestamps.Count >= requestsPerDay)
                {
                    session.Store(usage);
                    await session.SaveChangesAsync(ct);
                    return false;
                }
                waitUntil = GetWaitUntil(usage.RequestUtcTimestamps, now, requestsPerMinute, minimumInterval);
                if (waitUntil <= now)
                {
                    usage.RequestUtcTimestamps.Add(now);
                    session.Store(usage);
                    await session.SaveChangesAsync(ct);
                    return true;
                }
            }
            else
            {
                lock (_sync)
                {
                    if (!_localUsage.TryGetValue(usageId, out var timestamps)) _localUsage[usageId] = timestamps = [];
                    timestamps.RemoveAll(time => time <= now.AddHours(-24));
                    if (timestamps.Count >= requestsPerDay) return false;
                    waitUntil = GetWaitUntil(timestamps, now, requestsPerMinute, minimumInterval);
                    if (waitUntil <= now) { timestamps.Add(now); return true; }
                }
            }
            await Task.Delay(waitUntil.Value - now, ct);
        }
    }

    private static DateTimeOffset GetWaitUntil(IReadOnlyList<DateTimeOffset> timestamps, DateTimeOffset now, int requestsPerMinute, TimeSpan minimumInterval)
    {
        var waitUntil = timestamps.Count == 0 ? now : timestamps[^1] + minimumInterval;
        var recent = timestamps.Where(time => time > now.AddMinutes(-1)).OrderBy(time => time).ToArray();
        if (recent.Length >= requestsPerMinute)
            waitUntil = waitUntil >= recent[recent.Length - requestsPerMinute] + TimeSpan.FromMinutes(1)
                ? waitUntil
                : recent[recent.Length - requestsPerMinute] + TimeSpan.FromMinutes(1);
        return waitUntil;
    }

    private string GetUnavailablePoolDiagnostic(IReadOnlyList<ProviderKey> pool)
    {
        lock (_sync)
        {
            var disabled = pool.Count(key => _disabled.Contains((key.Provider, key.Slot)));
            var cooling = pool.Count(key => _cooldowns.TryGetValue((key.Provider, key.Slot), out var until) && until > DateTimeOffset.UtcNow);
            if (_providerCooldowns.TryGetValue((pool[0].Provider, GetModel(pool[0].Provider)), out var providerUntil) && providerUntil > DateTimeOffset.UtcNow)
                return $"{pool[0].Provider} model {GetModel(pool[0].Provider)} is rate limited; retry in about {Math.Max(1, (int)Math.Ceiling((providerUntil - DateTimeOffset.UtcNow).TotalSeconds))} seconds.";
            if (cooling == 0 && disabled > 0)
                return $"{pool[0].Provider}: all {pool.Count} configured key slots have been rejected and are disabled until the API restarts.";
            var remaining = _cooldowns.Where(pair => pair.Key.Provider == pool[0].Provider && pair.Value > DateTimeOffset.UtcNow).Select(pair => pair.Value).DefaultIfEmpty(DateTimeOffset.UtcNow.AddSeconds(1)).Min() - DateTimeOffset.UtcNow;
            return $"{pool[0].Provider}: all {pool.Count} configured key slots are unavailable ({disabled} rejected credentials, {cooling} in cooldown; retry in about {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} seconds).";
        }
    }

    private static string FormatFailure(ProviderKey key, int? status, ProviderRequestFailure failure, bool disabled, int cooldownSeconds)
    {
        var reason = failure.Malformed ? "invalid response" : status switch
        {
            401 or 403 => "credentials rejected",
            408 => "provider request timed out",
            429 => "rate limit or quota exceeded",
            >= 500 => "provider service error",
            null => "network or provider timeout",
            >= 400 => "request rejected",
            _ => "provider request failed"
        };
        var statusText = status.HasValue ? $" (HTTP {status.Value})" : string.Empty;
        var state = disabled ? " and is disabled until the API restarts" : cooldownSeconds > 0 ? $"; retry after about {cooldownSeconds} seconds" : string.Empty;
        var providerMessage = string.IsNullOrWhiteSpace(failure.ProviderMessage) ? string.Empty : $" — {failure.ProviderMessage}";
        return $"{key.Provider} key slot {key.Slot}: {reason}{statusText}{state}{providerMessage}.";
    }
}

public sealed class QueuedEntertainmentAiService(EntertainmentAiPromptQueue queue, EntertainmentAiCoordinator coordinator) : IGeminiSentenceService, IEntertainmentAnswerEvaluator
{
    public Task<GeminiSentence> GenerateAsync(string description, CefrLevel level, string apiKey, IReadOnlyList<string> previousSentences, CancellationToken ct = default) =>
        queue.EnqueueAsync(token => coordinator.GenerateAsync(description, level, previousSentences, token), ct);

    public Task<EntertainmentAnswerEvaluation> EvaluateAsync(string sentenceEn, string referenceFi, string answer, CefrLevel level, CancellationToken ct = default) =>
        queue.EnqueueAsync(token => coordinator.EvaluateAsync(sentenceEn, referenceFi, answer, level, token), ct);
}

public sealed class EntertainmentAiUsageWindow
{
    public string Id { get; set; } = string.Empty;
    public List<DateTimeOffset> RequestUtcTimestamps { get; set; } = [];
}
