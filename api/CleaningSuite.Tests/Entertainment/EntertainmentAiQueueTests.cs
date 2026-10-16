using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CleaningSuite.Tests.Entertainment;

public class EntertainmentAiQueueTests
{
    [Fact]
    public void KeyPoolsUseEveryConfiguredNumberedSlotAndIgnoreGapsAndDuplicates()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "g1",
            ["Entertainment:Gemini:Keys:3"] = "g3",
            ["Entertainment:Gemini:Keys:4"] = "g1",
            ["Entertainment:Gemini:ApiKey"] = "legacy",
            ["Entertainment:OpenAI:Keys:1"] = "o1",
            ["Entertainment:OpenAI:Keys:4"] = "o4"
        }).Build();

        Assert.Equal(new[] { "1", "3", "legacy-1" }, EntertainmentAiKeyPool.Gemini(config).Select(k => k.Slot));
        Assert.Equal(new[] { "1", "4" }, EntertainmentAiKeyPool.OpenAi(config).Select(k => k.Slot));
        Assert.Equal(3, EntertainmentAiKeyPool.Gemini(config).Count);
        Assert.Equal(2, EntertainmentAiKeyPool.OpenAi(config).Count);
    }

    [Fact]
    public async Task QueueRunsPromptsInFifoOrderAndNeverRunsTwoAtOnce()
    {
        using var queue = new EntertainmentAiPromptQueue();
        using var stopping = new CancellationTokenSource();
        await queue.StartAsync(stopping.Token);

        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();
        var active = 0;
        var maximumActive = 0;
        var first = queue.EnqueueAsync(async _ =>
        {
            lock (order) { order.Add(1); maximumActive = Math.Max(maximumActive, ++active); }
            firstEntered.SetResult();
            await releaseFirst.Task;
            lock (order) active--;
            return 1;
        }, stopping.Token);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = queue.EnqueueAsync(_ =>
        {
            lock (order) { order.Add(2); maximumActive = Math.Max(maximumActive, ++active); active--; }
            return Task.FromResult(2);
        }, stopping.Token);
        await Task.Yield();
        releaseFirst.SetResult();

        Assert.Equal(new[] { 1, 2 }, new[] { await first, await second });
        Assert.Equal(new[] { 1, 2 }, order);
        Assert.Equal(1, maximumActive);
        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CoordinatorTriesEveryRateLimitedGeminiKeyThenFallsBackToOpenAi()
    {
        var attempts = new List<string>();
        var handler = new FakeProviderHandler(request =>
        {
            var key = request.Headers.Contains("X-goog-api-key")
                ? request.Headers.GetValues("X-goog-api-key").Single()
                : request.Headers.Authorization!.Parameter!;
            attempts.Add(key);
            if (request.Headers.Contains("X-goog-api-key"))
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            var answer = "{\"sentenceEn\":\"The cat sleeps.\",\"sentenceFi\":\"Kissa nukkuu.\",\"hints\":[{\"english\":\"cat\",\"finnish\":\"kissa\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { output_text = answer }), Encoding.UTF8, "application/json")
            };
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "g1",
            ["Entertainment:Gemini:Keys:2"] = "g2",
            ["Entertainment:Gemini:Keys:3"] = "g3",
            ["Entertainment:OpenAI:Keys:1"] = "o1"
        }).Build();
        var coordinator = new EntertainmentAiCoordinator(new(new FakeHttpClientFactory(handler), config), config, NullLogger<EntertainmentAiCoordinator>.Instance);

        var result = await coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None);

        Assert.Equal("The cat sleeps.", result.SentenceEn);
        Assert.Equal(new[] { "g1", "g2", "g3", "o1" }, attempts);
    }

    [Fact]
    public async Task CoordinatorReturnsSafeProviderDiagnosticAndRedactsCredential()
    {
        const string secret = "gemini-secret-test-value";
        var handler = new FakeProviderHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { error = new { message = $"Quota exceeded for {secret}." } }), Encoding.UTF8, "application/json")
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = secret
        }).Build();
        var coordinator = new EntertainmentAiCoordinator(new(new FakeHttpClientFactory(handler), config), config, NullLogger<EntertainmentAiCoordinator>.Instance);

        var error = await Assert.ThrowsAsync<CleaningSuite.Application.Entertainment.EntertainmentAiUnavailableException>(
            () => coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None));

        Assert.Contains("Gemini key slot 1", error.Diagnostic);
        Assert.Contains("HTTP 429", error.Diagnostic);
        Assert.Contains("Quota exceeded", error.Diagnostic);
        Assert.DoesNotContain(secret, error.Diagnostic);
    }

    private sealed class FakeProviderHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
