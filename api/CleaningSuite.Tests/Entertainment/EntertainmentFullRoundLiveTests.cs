using System.Net.Http;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

/// <summary>Explicit bounded live provider smoke. Requires ENTERTAINMENT_LIVE_SMOKE=1
/// and a server-owned Gemini key. Ordinary CI does not call an external provider.</summary>
[Collection(GeminiProviderCollection.Name)]
public class EntertainmentFullRoundLiveTests
{
    [Fact]
    public async Task BoundedSmoke_GeneratesAndEvaluatesOneRoundWithGemini()
    {
        if (Environment.GetEnvironmentVariable("ENTERTAINMENT_LIVE_SMOKE") != "1") return;

        var values = new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:ApiKey"] = Environment.GetEnvironmentVariable("GAME_GEMINI_API_KEY"),
            ["Entertainment:Gemini:Model"] = Environment.GetEnvironmentVariable("GAME_GEMINI_MODEL"),
            ["Entertainment:Gemini:RateLimits:RequestsPerMinute"] = Environment.GetEnvironmentVariable("GAME_GEMINI_RPM") ?? "3",
            ["Entertainment:Gemini:RateLimits:RequestsPerDay"] = Environment.GetEnvironmentVariable("GAME_GEMINI_RPD") ?? "15"
        };
        AddNumberedKeys(values, "Entertainment:Gemini:Keys", "GEMINI_KEY", 10);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var geminiKeys = EntertainmentAiKeyPool.Gemini(configuration);
        Assert.NotEmpty(geminiKeys);

        using var logs = LoggerFactory.Create(builder => builder.AddConsole());
        Console.WriteLine($"Live game AI smoke: provider=Gemini; Gemini keys={geminiKeys.Count}; configured RPM/RPD={configuration["Entertainment:Gemini:RateLimits:RequestsPerMinute"]}/{configuration["Entertainment:Gemini:RateLimits:RequestsPerDay"]}.");
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
                $"Question generation failed with {geminiKeys.Count} unique Gemini key(s). " +
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
                $"Answer evaluation failed with {geminiKeys.Count} unique Gemini key(s). " +
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
