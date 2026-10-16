using System.Net;
using System.Text.Json;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CleaningSuite.Tests.Entertainment;
public class GeminiFallbackTests
{
    internal sealed class Provider(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request, ct); }
    }
    internal static IConfiguration Config(string? key = "server-secret") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Entertainment:Gemini:ApiKey"] = key, ["Entertainment:Gemini:TimeoutSeconds"] = "1" }).Build();
    internal static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(value) } } } } } })) };
    internal static object Sentence => new { sentenceEn = "The cat sleeps.", sentenceFi = "Kissa nukkuu.", hints = new[] { new { english = "cat", finnish = "kissa" }, new { english = "sleep", finnish = "nukkua" } } };
    [Fact] public async Task Server_key_is_used_and_legacy_key_ignored()
    {
        var provider = new Provider((request, _) => { Assert.Equal("server-secret", request.Headers.GetValues("X-goog-api-key").Single()); return Task.FromResult(Response(Sentence)); });
        var result = await new GeminiSentenceService(new(provider), Config()).GenerateAsync("cats", CefrLevel.A1, "user-secret", []);
        Assert.False(result.IsSample); Assert.Equal("kissa", result.Hints[0].Finnish); Assert.DoesNotContain("secret", JsonSerializer.Serialize(result));
    }
    [Fact] public async Task Missing_server_key_never_calls_provider()
    {
        var provider = new Provider((_, _) => throw new Exception());
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiSentenceService(new(provider), Config(null)).GenerateAsync("cats", CefrLevel.A1, "user-secret", [])); Assert.Equal(0, provider.Calls);
    }
    [Theory] [InlineData(400, 1)] [InlineData(401, 1)] [InlineData(429, 2)] [InlineData(503, 2)]
    public async Task Http_failure_is_safe_and_retry_bounded(int status, int calls)
    {
        var provider = new Provider((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var failure = await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiSentenceService(new(provider), Config()).GenerateAsync("cats", CefrLevel.A1, "user-secret", []));
        Assert.Equal(calls, provider.Calls); Assert.DoesNotContain("secret", failure.Message);
    }
    [Fact] public async Task Network_and_timeout_fail_safely()
    {
        var network = new Provider((_, _) => throw new HttpRequestException("server-secret"));
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiSentenceService(new(network), Config()).GenerateAsync("cats", CefrLevel.A1, "", [])); Assert.Equal(2, network.Calls);
        var timeout = new Provider(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Response(Sentence); });
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiSentenceService(new(timeout), Config()).GenerateAsync("cats", CefrLevel.A1, "", [])); Assert.Equal(2, timeout.Calls);
    }
    [Theory] [InlineData("not-json")] [InlineData("{}")] [InlineData("{\"candidates\":[]}")]
    public async Task Malformed_output_fails_without_sample(string json)
    {
        var provider = new Provider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiSentenceService(new(provider), Config()).GenerateAsync("cats", CefrLevel.A1, "", [])); Assert.Equal(1, provider.Calls);
    }
    [Fact] public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var provider = new Provider((_, _) => throw new Exception());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GeminiSentenceService(new(provider), Config()).GenerateAsync("cats", CefrLevel.A1, "", [], cts.Token)); Assert.Equal(0, provider.Calls);
    }
}
