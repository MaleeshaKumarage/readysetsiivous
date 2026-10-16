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
    public void GeminiKeyPoolUsesConfiguredNumberedSlotsAndIgnoresGapsAndDuplicates()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "g1",
            ["Entertainment:Gemini:Keys:3"] = "g3",
            ["Entertainment:Gemini:Keys:4"] = "g1",
            ["Entertainment:Gemini:ApiKey"] = "legacy",
        }).Build();

        Assert.Equal(new[] { "1", "3", "legacy-1" }, EntertainmentAiKeyPool.Gemini(config).Select(k => k.Slot));
        Assert.Equal(3, EntertainmentAiKeyPool.Gemini(config).Count);
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
    public async Task CoordinatorStopsKeyRotationAndOpenAiAfterSharedQuotaExhaustion()
    {
        var attempts = new List<string>();
        var handler = new FakeProviderHandler(request =>
        {
            var key = request.Headers.Contains("X-goog-api-key")
                ? request.Headers.GetValues("X-goog-api-key").Single()
                : request.Headers.Authorization!.Parameter!;
            attempts.Add(key);
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"quota exceeded\"}}", Encoding.UTF8, "application/json")
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

        var error = await Assert.ThrowsAsync<CleaningSuite.Application.Entertainment.EntertainmentAiUnavailableException>(
            () => coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None));
        var secondError = await Assert.ThrowsAsync<CleaningSuite.Application.Entertainment.EntertainmentAiUnavailableException>(
            () => coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None));

        Assert.Contains("quota exhausted", error.Diagnostic);
        Assert.Contains("cooling down", secondError.Diagnostic);
        Assert.Equal(new[] { "g1" }, attempts);
    }

    [Fact]
    public async Task QueuedRequestsShareModelCooldownAfterFirstQuotaFailure()
    {
        var attempts = 0;
        var handler = new FakeProviderHandler(_ =>
        {
            Interlocked.Increment(ref attempts);
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"quota exceeded\"}}", Encoding.UTF8, "application/json")
            };
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "g1"
        }).Build();
        var coordinator = new EntertainmentAiCoordinator(new(new FakeHttpClientFactory(handler), config), config, NullLogger<EntertainmentAiCoordinator>.Instance);
        using var queue = new EntertainmentAiPromptQueue();
        using var stopping = new CancellationTokenSource();
        await queue.StartAsync(stopping.Token);
        var service = new QueuedEntertainmentAiService(queue, coordinator);

        var first = service.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, string.Empty, [], stopping.Token);
        var second = service.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, string.Empty, [], stopping.Token);
        await Assert.ThrowsAsync<CleaningSuite.Application.Entertainment.EntertainmentAiUnavailableException>(() => first);
        await Assert.ThrowsAsync<CleaningSuite.Application.Entertainment.EntertainmentAiUnavailableException>(() => second);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task CoordinatorEnforcesConfiguredRollingDailyRequestBudget()
    {
        var attempts = 0;
        var handler = new FakeProviderHandler(_ =>
        {
            attempts++;
            var answer = "{\"sentenceEn\":\"The cat sleeps.\",\"sentenceFi\":\"Kissa nukkuu.\",\"hints\":[{\"english\":\"cat\",\"finnish\":\"kissa\"}]}";
            return GeminiSuccess(answer);
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "g1",
            ["Entertainment:Gemini:RateLimits:RequestsPerMinute"] = "1000",
            ["Entertainment:Gemini:RateLimits:RequestsPerDay"] = "1"
        }).Build();
        var coordinator = new EntertainmentAiCoordinator(new(new FakeHttpClientFactory(handler), config), config, NullLogger<EntertainmentAiCoordinator>.Instance);
        await coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None);

        var error = await Assert.ThrowsAsync<CleaningSuite.Application.Entertainment.EntertainmentAiUnavailableException>(
            () => coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None));

        Assert.Equal(1, attempts);
        Assert.Contains("rolling 24-hour request budget", error.Diagnostic);
    }

    [Fact]
    public async Task CoordinatorSmoothsRequestsToStayWithinConfiguredRequestsPerMinute()
    {
        var requestTimes = new List<DateTimeOffset>();
        var handler = new FakeProviderHandler(_ =>
        {
            requestTimes.Add(DateTimeOffset.UtcNow);
            var answer = "{\"sentenceEn\":\"The cat sleeps.\",\"sentenceFi\":\"Kissa nukkuu.\",\"hints\":[{\"english\":\"cat\",\"finnish\":\"kissa\"}]}";
            return GeminiSuccess(answer);
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "g1",
            ["Entertainment:Gemini:RateLimits:RequestsPerMinute"] = "60",
            ["Entertainment:Gemini:RateLimits:RequestsPerDay"] = "10"
        }).Build();
        var coordinator = new EntertainmentAiCoordinator(new(new FakeHttpClientFactory(handler), config), config, NullLogger<EntertainmentAiCoordinator>.Instance);
        await coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None);
        await coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None);
        Assert.True((requestTimes[1] - requestTimes[0]).TotalMilliseconds >= 900);
    }

    [Fact]
    public async Task CoordinatorReturnsSafeProviderDiagnosticAndRedactsCredential()
    {
        const string secret = "gemini-secret-test-value";
        var handler = new FakeProviderHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { error = new { status = "QUOTA_EXCEEDED", message = $"Quota exceeded for {secret}." } }), Encoding.UTF8, "application/json")
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

    [Fact]
    public async Task CoordinatorTriesNextGeminiKeyOnlyForExplicitInvalidCredential()
    {
        var attempts = new List<string>();
        var handler = new FakeProviderHandler(request =>
        {
            var key = request.Headers.GetValues("X-goog-api-key").Single();
            attempts.Add(key);
            if (key == "bad") return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":{\"status\":\"PERMISSION_DENIED\",\"message\":\"API key not valid\"}}", Encoding.UTF8, "application/json")
            };
            var answer = "{\"sentenceEn\":\"The cat sleeps.\",\"sentenceFi\":\"Kissa nukkuu.\",\"hints\":[{\"english\":\"cat\",\"finnish\":\"kissa\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = answer } } } } } }), Encoding.UTF8, "application/json") };
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "bad",
            ["Entertainment:Gemini:Keys:2"] = "good",
            ["Entertainment:Gemini:RateLimits:RequestsPerMinute"] = "1000"
        }).Build();
        var coordinator = new EntertainmentAiCoordinator(new(new FakeHttpClientFactory(handler), config), config, NullLogger<EntertainmentAiCoordinator>.Instance);

        var result = await coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None);

        Assert.Equal("The cat sleeps.", result.SentenceEn);
        Assert.Equal(new[] { "bad", "good" }, attempts);
    }

    [Fact]
    public async Task ProviderRetriesTransientServiceFailureOnlyOnce()
    {
        var attempts = 0;
        var handler = new FakeProviderHandler(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return GeminiSuccess("{\"sentenceEn\":\"The cat sleeps.\",\"sentenceFi\":\"Kissa nukkuu.\",\"hints\":[{\"english\":\"cat\",\"finnish\":\"kissa\"}]}");
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:Keys:1"] = "g1"
        }).Build();
        var coordinator = new EntertainmentAiCoordinator(new(new FakeHttpClientFactory(handler), config), config, NullLogger<EntertainmentAiCoordinator>.Instance);

        var result = await coordinator.GenerateAsync("cats", CleaningSuite.Domain.Entertainment.CefrLevel.A1, [], CancellationToken.None);

        Assert.Equal("The cat sleeps.", result.SentenceEn);
        Assert.Equal(2, attempts);
    }

    private sealed class FakeProviderHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private static HttpResponseMessage GeminiSuccess(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } }), Encoding.UTF8, "application/json")
    };

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
