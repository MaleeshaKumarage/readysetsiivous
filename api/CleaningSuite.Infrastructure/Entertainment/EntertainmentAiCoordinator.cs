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
        var pool = EntertainmentAiKeyPool.Gemini(config);
        var diagnostics = new List<string>();
        if (pool.Count == 0)
        {
            logger.LogWarning("Game AI unavailable: no Gemini credentials are configured on the server.");
            throw new EntertainmentAiUnavailableException(diagnostic: "No Gemini API keys are configured on the server.");
        }

        var keys = Rotate(pool);
        if (keys.Count == 0) diagnostics.Add(GetUnavailablePoolDiagnostic(pool));
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            var budgetNextAvailable = await ReserveProviderRequestAsync(ct);
            if (budgetNextAvailable is { } nextAvailable)
            {
                diagnostics.Add($"Gemini model {GetModel()} has reached the configured rolling 24-hour request budget; the next request can run after {nextAvailable:O}.");
                logger.LogWarning("Game AI {Operation} skipped Gemini model {Model}: rolling 24-hour budget exhausted until {NextAvailableUtc}.", operation, GetModel(), nextAvailable);
                break;
            }
            var elapsed = Stopwatch.StartNew();
            try
            {
                using var result = await transport.SendAsync(key, instruction, input, schema, ct, ReserveRetryAsync);
                var parsed = parse(result);
                MarkHealthy(key);
                logger.LogInformation("Game AI {Operation} succeeded with Gemini slot {Slot} in {ElapsedMilliseconds} ms.", operation, key.Slot, elapsed.ElapsedMilliseconds);
                return parsed;
            }
            catch (ProviderBudgetExhaustedException exhausted)
            {
                diagnostics.Add($"Gemini model {GetModel()} has reached the configured rolling 24-hour request budget; the next request can run after {exhausted.NextAvailable:O}.");
                logger.LogWarning("Game AI {Operation} retry skipped: rolling 24-hour budget exhausted until {NextAvailableUtc}.", operation, exhausted.NextAvailable);
                break;
            }
            catch (ProviderRequestFailure failure)
            {
                var status = (int?)failure.Status;
                var disabled = false;
                var cooldown = TimeSpan.Zero;
                var stop = true;

                if (failure.Kind == GeminiFailureKind.InvalidCredential)
                {
                    Disable(key);
                    disabled = true;
                    stop = false;
                }
                else if (status == 429)
                {
                    cooldown = QuotaCooldown(failure);
                    CooldownProvider(key, cooldown);
                }
                else if (failure.Kind == GeminiFailureKind.ProjectAccessDenied)
                {
                    cooldown = TimeSpan.FromMinutes(5);
                    CooldownProvider(key, cooldown);
                }
                else if (failure.Kind == GeminiFailureKind.ProviderUnavailable || status is null or 408 or >= 500)
                {
                    cooldown = failure.RetryAfter ?? TimeSpan.FromSeconds(30);
                    CooldownProvider(key, cooldown);
                }
                else if (failure.Malformed)
                {
                    cooldown = TimeSpan.FromSeconds(5);
                    CooldownProvider(key, cooldown);
                }

                var cooldownSeconds = cooldown > TimeSpan.Zero ? (int)Math.Clamp(Math.Ceiling(cooldown.TotalSeconds), 1, 86400) : 0;
                diagnostics.Add(FormatFailure(key, status, failure, disabled, cooldownSeconds));
                logger.LogWarning("Game AI {Operation} failed with Gemini slot {Slot}, category {Category}, status {Status}, code {ProviderCode}, reason {ProviderReason}, cooldown {CooldownSeconds}s, disabled {Disabled}, elapsed {ElapsedMilliseconds} ms.", operation, key.Slot, failure.Kind, status, failure.ProviderCode, failure.ProviderReason, cooldownSeconds, disabled, elapsed.ElapsedMilliseconds);
                if (stop) break;
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or EntertainmentAiUnavailableException)
            {
                CooldownProvider(key, TimeSpan.FromSeconds(5));
                diagnostics.Add($"Gemini model {GetModel()}: the provider returned a response that did not pass game validation.");
                logger.LogWarning("Game AI {Operation} returned invalid structured output from Gemini slot {Slot} in {ElapsedMilliseconds} ms.", operation, key.Slot, elapsed.ElapsedMilliseconds);
                break;
            }
        }
        var diagnostic = string.Join(Environment.NewLine, diagnostics.Distinct(StringComparer.Ordinal));
        if (diagnostic.Length > 2400) diagnostic = diagnostic[..2400] + "…";
        throw new EntertainmentAiUnavailableException(diagnostic: string.IsNullOrWhiteSpace(diagnostic) ? "No configured provider key is currently available. Retry shortly." : diagnostic);
    }

    private sealed class ProviderBudgetExhaustedException(DateTimeOffset nextAvailable) : Exception
    {
        public DateTimeOffset NextAvailable { get; } = nextAvailable;
    }

    private async Task ReserveRetryAsync(CancellationToken ct)
    {
        if (await ReserveProviderRequestAsync(ct) is { } nextAvailable)
            throw new ProviderBudgetExhaustedException(nextAvailable);
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
            foreach (var expired in _providerCooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) _providerCooldowns.Remove(expired);
            return Enumerable.Range(0, pool.Count).Select(i => pool[(start + i) % pool.Count])
                .Where(k => !_disabled.Contains((k.Provider, k.Slot)) && !_providerCooldowns.ContainsKey((k.Provider, GetModel()))).ToArray();
        }
    }

    private void CooldownProvider(ProviderKey key, TimeSpan duration) { lock (_sync) _providerCooldowns[(key.Provider, GetModel())] = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 1, 86400)); }
    private void Disable(ProviderKey key) { lock (_sync) _disabled.Add((key.Provider, key.Slot)); }
    private void MarkHealthy(ProviderKey key) { lock (_sync) _providerCooldowns.Remove((key.Provider, GetModel())); }

    private int GetLimit(string provider, string limit, int fallback, int maximum) =>
        Math.Clamp(config.GetValue<int?>($"Entertainment:{provider}:RateLimits:{limit}") ?? fallback, 1, maximum);

    private string GetModel() => config["Entertainment:Gemini:Model"] ?? "gemini-3.5-flash-lite";

    private async Task<DateTimeOffset?> ReserveProviderRequestAsync(CancellationToken ct)
    {
        var requestsPerMinute = GetLimit("Gemini", "RequestsPerMinute", 3, 1000);
        var requestsPerDay = GetLimit("Gemini", "RequestsPerDay", 15, 100000);
        var minimumInterval = TimeSpan.FromSeconds(60d / requestsPerMinute);
        var usageId = $"Gemini:{GetModel()}";
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
                    var nextAvailable = usage.RequestUtcTimestamps.Min().AddHours(24);
                    session.Store(usage);
                    await session.SaveChangesAsync(ct);
                    return nextAvailable;
                }
                waitUntil = GetWaitUntil(usage.RequestUtcTimestamps, now, requestsPerMinute, minimumInterval);
                if (waitUntil <= now)
                {
                    usage.RequestUtcTimestamps.Add(now);
                    session.Store(usage);
                    await session.SaveChangesAsync(ct);
                    return null;
                }
            }
            else
            {
                lock (_sync)
                {
                    if (!_localUsage.TryGetValue(usageId, out var timestamps)) _localUsage[usageId] = timestamps = [];
                    timestamps.RemoveAll(time => time <= now.AddHours(-24));
                    if (timestamps.Count >= requestsPerDay) return timestamps.Min().AddHours(24);
                    waitUntil = GetWaitUntil(timestamps, now, requestsPerMinute, minimumInterval);
                    if (waitUntil <= now) { timestamps.Add(now); return null; }
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
            if (_providerCooldowns.TryGetValue((pool[0].Provider, GetModel()), out var providerUntil) && providerUntil > DateTimeOffset.UtcNow)
                return $"Gemini model {GetModel()} is temporarily cooling down; retry in about {Math.Max(1, (int)Math.Ceiling((providerUntil - DateTimeOffset.UtcNow).TotalSeconds))} seconds.";
            var disabled = pool.Count(key => _disabled.Contains((key.Provider, key.Slot)));
            if (disabled > 0)
                return $"{pool[0].Provider}: all {pool.Count} configured key slots have been rejected and are disabled until the API restarts.";
            return $"Gemini model {GetModel()} is temporarily unavailable; retry later.";
        }
    }

    private static TimeSpan QuotaCooldown(ProviderRequestFailure failure)
    {
        if (failure.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero) return retryAfter;
        if (IsDailyQuota(failure))
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
                var localNow = TimeZoneInfo.ConvertTime(now, pacific);
                var nextLocalMidnight = DateTime.SpecifyKind(localNow.Date.AddDays(1), DateTimeKind.Unspecified);
                var nextResetUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, pacific);
                return new DateTimeOffset(nextResetUtc, TimeSpan.Zero) - now;
            }
            catch (TimeZoneNotFoundException) { return TimeSpan.FromHours(24); }
            catch (InvalidTimeZoneException) { return TimeSpan.FromHours(24); }
        }
        return failure.Kind == GeminiFailureKind.QuotaExceeded ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(1);
    }

    private static bool IsDailyQuota(ProviderRequestFailure failure)
    {
        var text = $"{failure.ProviderCode} {failure.ProviderReason} {failure.ProviderMessage}";
        return failure.Kind == GeminiFailureKind.QuotaExceeded &&
            (text.Contains("daily", StringComparison.OrdinalIgnoreCase) || text.Contains("per day", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatFailure(ProviderKey key, int? status, ProviderRequestFailure failure, bool disabled, int cooldownSeconds)
    {
        var reason = failure.Malformed ? "invalid response" : failure.Kind switch
        {
            GeminiFailureKind.RateLimited => "request rate limited",
            GeminiFailureKind.QuotaExceeded => "project or daily quota exhausted",
            GeminiFailureKind.UnknownRateLimit => "Gemini returned an unclassified 429; this model is cooling down",
            GeminiFailureKind.InvalidCredential => "Gemini credential rejected",
            GeminiFailureKind.ProjectAccessDenied => "Gemini project/model access denied",
            GeminiFailureKind.InvalidRequest => "Gemini rejected the request or model configuration",
            GeminiFailureKind.ProviderUnavailable => "Gemini service or network unavailable",
            _ => status switch { >= 400 => "Gemini request rejected", _ => "Gemini provider request failed" }
        };
        var statusText = status.HasValue ? $" (HTTP {status.Value})" : string.Empty;
        var state = disabled ? " and is disabled until the API restarts" :
            failure.Kind == GeminiFailureKind.QuotaExceeded && failure.RetryAfter is null && !IsDailyQuota(failure)
                ? $"; Gemini did not provide a reliable reset time; retry later (internal cooldown about {cooldownSeconds} seconds)"
                : cooldownSeconds > 0 ? $"; internal cooldown is about {cooldownSeconds} seconds" : string.Empty;
        var providerCode = string.IsNullOrWhiteSpace(failure.ProviderCode) ? string.Empty : $" [{failure.ProviderCode}]";
        var providerReason = string.IsNullOrWhiteSpace(failure.ProviderReason) ? string.Empty : $" ({failure.ProviderReason})";
        var providerMessage = string.IsNullOrWhiteSpace(failure.ProviderMessage) ? string.Empty : $" — {failure.ProviderMessage}";
        return $"Gemini key slot {key.Slot}: {reason}{statusText}{providerCode}{providerReason}{state}{providerMessage}.";
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
