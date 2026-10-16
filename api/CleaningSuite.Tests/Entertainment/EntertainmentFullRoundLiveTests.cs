using System.Net.Http;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

/// <summary>Explicit bounded live provider smoke. Requires ENTERTAINMENT_LIVE_SMOKE=1
/// and server-owned Gemini or OpenAI keys. Ordinary CI does not call an external provider.</summary>
[Collection(GeminiProviderCollection.Name)]
public class EntertainmentFullRoundLiveTests
{
    [Fact]
    public async Task BoundedSmoke_GeneratesAndEvaluatesOneRoundWithConfiguredProviders()
    {
        if (Environment.GetEnvironmentVariable("ENTERTAINMENT_LIVE_SMOKE") != "1") return;

        var selectedProvider = Environment.GetEnvironmentVariable("ENTERTAINMENT_LIVE_PROVIDER")?.ToLowerInvariant() ?? "both";
        var useGemini = selectedProvider is "both" or "gemini";
        var useOpenAi = selectedProvider is "both" or "openai";
        Assert.True(useGemini || useOpenAi, $"Unsupported live provider '{selectedProvider}'.");
        var values = new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:ApiKey"] = useGemini ? Environment.GetEnvironmentVariable("GAME_GEMINI_API_KEY") : null,
            ["Entertainment:Gemini:Model"] = Environment.GetEnvironmentVariable("GAME_GEMINI_MODEL"),
            ["Entertainment:OpenAI:ApiKey"] = useOpenAi ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") : null,
            ["Entertainment:Gemini:RateLimits:RequestsPerMinute"] = Environment.GetEnvironmentVariable("GAME_GEMINI_RPM") ?? "3",
            ["Entertainment:Gemini:RateLimits:RequestsPerDay"] = Environment.GetEnvironmentVariable("GAME_GEMINI_RPD") ?? "15",
            ["Entertainment:OpenAI:Model"] = Environment.GetEnvironmentVariable("GAME_OPENAI_MODEL") ?? "gpt-4.1-mini",
            ["Entertainment:OpenAI:RateLimits:RequestsPerMinute"] = Environment.GetEnvironmentVariable("GAME_OPENAI_RPM") ?? "5",
            ["Entertainment:OpenAI:RateLimits:RequestsPerDay"] = Environment.GetEnvironmentVariable("GAME_OPENAI_RPD") ?? "50"
        };
        if (useGemini) AddNumberedKeys(values, "Entertainment:Gemini:Keys", "GEMINI_KEY", 10);
        if (useOpenAi) AddNumberedKeys(values, "Entertainment:OpenAI:Keys", "OPENAI_API_KEY", 10);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var geminiKeys = EntertainmentAiKeyPool.Gemini(configuration);
        var openAiKeys = EntertainmentAiKeyPool.OpenAi(configuration);
        IEnumerable<ProviderKey> selectedKeys = useGemini && useOpenAi ? geminiKeys.Concat(openAiKeys) : useGemini ? geminiKeys : openAiKeys;
        Assert.NotEmpty(selectedKeys);

        using var logs = LoggerFactory.Create(builder => builder.AddConsole());
        Console.WriteLine($"Live game AI smoke: provider={selectedProvider}; Gemini keys={geminiKeys.Count}, OpenAI keys={openAiKeys.Count}; configured RPM/RPD Gemini={configuration["Entertainment:Gemini:RateLimits:RequestsPerMinute"]}/{configuration["Entertainment:Gemini:RateLimits:RequestsPerDay"]}, OpenAI={configuration["Entertainment:OpenAI:RateLimits:RequestsPerMinute"]}/{configuration["Entertainment:OpenAI:RateLimits:RequestsPerDay"]}.");
        var coordinator = new EntertainmentAiCoordinator(
            new EntertainmentAiProviderTransport(new LiveHttpClientFactory(), configuration, logs.CreateLogger<EntertainmentAiProviderTransport>()),
            configuration,
            logs.CreateLogger<EntertainmentAiCoordinator>());

        GeminiSentence round;
        try
        {
            round = await coordinator.GenerateAsync("Travel English", CefrLevel.B1, [], CancellationToken.None);
        }
        catch (EntertainmentAiUnavailableException error)
        {
            throw new Xunit.Sdk.XunitException(
                $"Question generation failed with {geminiKeys.Count} unique Gemini key(s) and {openAiKeys.Count} unique OpenAI key(s). " +
                (error.Diagnostic ?? error.Message));
        }
        Assert.False(string.IsNullOrWhiteSpace(round.SentenceEn));
        Assert.False(string.IsNullOrWhiteSpace(round.SentenceFi));
        Assert.NotEmpty(round.Hints);

        EntertainmentAnswerEvaluation evaluation;
        try
        {
            evaluation = await coordinator.EvaluateAsync(round.SentenceEn, round.SentenceFi, round.SentenceFi, CefrLevel.B1, CancellationToken.None);
        }
        catch (EntertainmentAiUnavailableException error)
        {
            throw new Xunit.Sdk.XunitException(
                $"Answer evaluation failed with {geminiKeys.Count} unique Gemini key(s) and {openAiKeys.Count} unique OpenAI key(s). " +
                (error.Diagnostic ?? error.Message));
        }
        Assert.True(evaluation.IsCorrect);
        Assert.Equal(10, EntertainmentScoring.ScoreAnswer(evaluation));
        Assert.NotNull(evaluation.GrammarRules);
    }

    private static void AddNumberedKeys(IDictionary<string, string?> values, string configPath, string envPrefix, int count)
    {
        for (var index = 1; index <= count; index++)
            values[$"{configPath}:{index}"] = Environment.GetEnvironmentVariable($"{envPrefix}_{index}");
    }

    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
