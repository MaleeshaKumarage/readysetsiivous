using System.Net.Http;
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

        var values = new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:ApiKey"] = Environment.GetEnvironmentVariable("GAME_GEMINI_API_KEY")
        };
        AddNumberedKeys(values, "Entertainment:Gemini:Keys", "GEMINI_KEY", 10);
        AddNumberedKeys(values, "Entertainment:OpenAI:Keys", "OPENAI_API_KEY", 10);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.NotEmpty(EntertainmentAiKeyPool.Gemini(configuration).Concat(EntertainmentAiKeyPool.OpenAi(configuration)));

        var coordinator = new EntertainmentAiCoordinator(
            new EntertainmentAiProviderTransport(new LiveHttpClientFactory(), configuration),
            configuration,
            NullLogger<EntertainmentAiCoordinator>.Instance);

        var round = await coordinator.GenerateAsync("Travel English", CefrLevel.B1, [], CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(round.SentenceEn));
        Assert.False(string.IsNullOrWhiteSpace(round.SentenceFi));
        Assert.NotEmpty(round.Hints);

        var evaluation = await coordinator.EvaluateAsync(round.SentenceEn, round.SentenceFi, round.SentenceFi, CefrLevel.B1, CancellationToken.None);
        Assert.True(evaluation.IsCorrect);
        Assert.Equal(10, EntertainmentScoring.ScoreAnswer(evaluation));
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
