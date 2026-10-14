using System.Net;
using System.Text.Json;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CleaningSuite.Tests.Entertainment;

public class GeminiFallbackTests
{
    private sealed class Provider(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(ct); }
    }
    private static GeminiSentenceService Service(HttpMessageHandler handler, int seconds = 1) => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Entertainment:Gemini:TimeoutSeconds"] = seconds.ToString() }).Build());
    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(value) } } } } } }))
    };
    private sealed class IgnoresCancellation(Action cancel) : IGeminiSentenceService
    {
        public Task<GeminiSentence> GenerateAsync(string description, CefrLevel level, string apiKey, IReadOnlyList<string> previousSentences, CancellationToken ct = default)
        { cancel(); return Task.FromResult(SampleSentenceCatalog.Select(level, previousSentences)); }
    }

    [Fact]
    public async Task Valid_provider_output_is_preserved_including_Finnish_hint_objects()
    {
        var provider = new Provider(_ => Task.FromResult(Response(new { sentenceEn = "The cat sleeps.", sentenceFi = "Kissa nukkuu.", baseForms = new[] { new { fi = "kissa" }, new { fi = "nukkua" } } })));
        var result = await Service(provider).GenerateAsync("animals", CefrLevel.A1, "test-key", Array.Empty<string>());
        Assert.False(result.IsSample); Assert.Equal("The cat sleeps.", result.SentenceEn);
        Assert.Equal("Kissa nukkuu.", result.SentenceFi); Assert.Equal(new[] { "kissa", "nukkua" }, result.BaseForms); Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(429)] [InlineData(500)] [InlineData(502)] [InlineData(503)] [InlineData(504)]
    public async Task Http_failures_return_samples_without_repeated_vendor_requests(int status)
    {
        var provider = new Provider(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var result = await Service(provider).GenerateAsync("work", CefrLevel.B1, "test-key", Array.Empty<string>());
        Assert.True(result.IsSample); Assert.NotEmpty(result.SentenceFi); Assert.NotEmpty(result.BaseForms); Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Network_failure_and_provider_deadline_return_samples()
    {
        var failed = new Provider(_ => throw new HttpRequestException("Unavailable"));
        Assert.True((await Service(failed).GenerateAsync("work", CefrLevel.A2, "test-key", Array.Empty<string>())).IsSample);
        var delayed = new Provider(async ct => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(HttpStatusCode.OK); });
        Assert.True((await Service(delayed).GenerateAsync("work", CefrLevel.A2, "test-key", Array.Empty<string>())).IsSample);
        Assert.Equal(1, delayed.Calls);
    }

    [Theory]
    [InlineData("not-json")] [InlineData("{}")] [InlineData("{\"candidates\":[]}")]
    public async Task Malformed_provider_envelopes_return_samples(string json)
    {
        var provider = new Provider(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
        Assert.True((await Service(provider).GenerateAsync("work", CefrLevel.B2, "test-key", Array.Empty<string>())).IsSample);
    }

    [Theory]
    [InlineData(" ", "Kissa nukkuu.", "kissa")]
    [InlineData("The cat sleeps.", " ", "kissa")]
    [InlineData("The cat sleeps.", "Kissa nukkuu.", " ")]
    public async Task Blank_sentence_translation_or_hint_returns_sample(string english, string finnish, string hint)
    {
        var provider = new Provider(_ => Task.FromResult(Response(new { sentenceEn = english, sentenceFi = finnish, baseForms = new[] { hint } })));
        Assert.True((await Service(provider).GenerateAsync("work", CefrLevel.B2, "test-key", Array.Empty<string>())).IsSample);
    }

    [Theory]
    [InlineData("{\"sentenceEn\":\"Hello.\"}")]
    [InlineData("{\"sentenceEn\":12,\"sentenceFi\":\"Hei.\",\"baseForms\":[\"hei\"]}")]
    [InlineData("{\"sentenceEn\":\"Hello.\",\"sentenceFi\":\"Hei.\",\"baseForms\":[]}")]
    [InlineData("{\"sentenceEn\":\"Hello.\",\"sentenceFi\":\"Hei.\",\"baseForms\":[null]}")]
    [InlineData("{\"sentenceEn\":\"Hello.\",\"sentenceFi\":\"Hei.\",\"baseForms\":[{\"en\":\"hello\"}]}")]
    public async Task Missing_and_wrong_field_types_return_samples(string innerJson)
    {
        var provider = new Provider(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
            JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = innerJson } } } } } })) }));
        Assert.True((await Service(provider).GenerateAsync("work", CefrLevel.C1, "test-key", Array.Empty<string>())).IsSample);
    }

    [Fact]
    public async Task Caller_cancellation_and_unexpected_programming_errors_are_not_fallbacks()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var provider = new Provider(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(provider).GenerateAsync("work", CefrLevel.A1, "test-key", Array.Empty<string>(), canceled.Token));
        Assert.Equal(0, provider.Calls);
        var broken = new Provider(_ => throw new InvalidOperationException("Unexpected programming failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(broken).GenerateAsync("work", CefrLevel.A1, "test-key", Array.Empty<string>()));
    }

    [Theory]
    [InlineData(CefrLevel.A1)] [InlineData(CefrLevel.A2)] [InlineData(CefrLevel.B1)] [InlineData(CefrLevel.B2)] [InlineData(CefrLevel.C1)] [InlineData(CefrLevel.C2)]
    public void Every_level_has_twelve_complete_samples_and_avoids_the_recent_ten(CefrLevel level)
    {
        var history = new List<string>();
        for (var index = 0; index < 12; index++) {
            var sample = SampleSentenceCatalog.Select(level, history);
            Assert.True(sample.IsSample); Assert.NotEmpty(sample.SentenceEn); Assert.NotEmpty(sample.SentenceFi);
            Assert.NotEmpty(sample.BaseForms); Assert.All(sample.BaseForms, hint => Assert.False(string.IsNullOrWhiteSpace(hint)));
            Assert.DoesNotContain(sample.SentenceEn, history); history.Add(sample.SentenceEn);
        }
        var recent = history.AsEnumerable().Reverse().Take(10).Select(s => "  " + s.ToUpperInvariant() + " ").ToList();
        Assert.DoesNotContain(SampleSentenceCatalog.Select(level, recent).SentenceEn.ToUpperInvariant(), recent.Select(s => s.Trim()));
        Assert.Equal(history[0], SampleSentenceCatalog.Select(level, history.AsEnumerable().Reverse().ToList()).SentenceEn);
    }

    [Fact]
    public void Invalid_level_remains_an_error() => Assert.Throws<ArgumentOutOfRangeException>(() => SampleSentenceCatalog.Select((CefrLevel)99, Array.Empty<string>()));

    [Theory]
    [InlineData(true, true, false)] [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(false, false, false)]
    [InlineData(true, false, true)] [InlineData(false, false, true)]
    public async Task Real_handlers_cancel_before_start_or_during_generation_without_game_mutation_or_saves(bool firstRound, bool preCanceled, bool ignoresCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<IEntertainmentRepository>(); var protector = new Mock<IEntertainmentKeyProtector>();
        var group = EntertainmentGroup.Create("Game", "encrypted", new string('x', 200), "test-join-key");
        var player = EntertainmentPlayer.Create(group.Id, "Alice", CefrLevel.B1, true);
        repository.Setup(x => x.GetGroupAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        repository.Setup(x => x.GetPlayerAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        repository.Setup(x => x.ListPlayersAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { player });
        repository.Setup(x => x.ListRecentSentencesAsync(group.Id, 10, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        protector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("test-key");
        var provider = new Provider(_ => { cancellation.Cancel(); throw new HttpRequestException("Provider returned after caller cancellation"); });
        if (preCanceled) cancellation.Cancel();
        IGeminiSentenceService service = ignoresCancellation ? new IgnoresCancellation(cancellation.Cancel) : Service(provider);
        if (firstRound) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StartEntertainmentGameCommandHandler(repository.Object, protector.Object, service).Handle(new(group.Id, player.Id), cancellation.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StartNextEntertainmentRoundCommandHandler(repository.Object, protector.Object, service).Handle(new(group.Id, player.Id, 2), cancellation.Token));
        Assert.Equal(EntertainmentStatus.Lobby, group.Status);
        repository.Verify(x => x.SaveGroupAsync(It.IsAny<EntertainmentGroup>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.SaveRoundAsync(It.IsAny<EntertainmentRound>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(preCanceled || ignoresCancellation ? 0 : 1, provider.Calls);
    }
}
