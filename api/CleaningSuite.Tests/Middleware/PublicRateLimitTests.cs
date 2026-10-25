using Microsoft.AspNetCore.Builder;
using System.Net;
using CleaningSuite.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CleaningSuite.Tests.Middleware;

public sealed class PublicRateLimitTests
{
    [Fact]
    public async Task Thirty_requests_then_retry_after_and_exact_window_reset()
    {
        var time = new Clock();
        var limiter = Limiter(time);
        for (var i = 0; i < 30; i++) Assert.Equal(200, await Hit(limiter, "192.0.2.1"));
        var denied = Context("192.0.2.1");
        await limiter.InvokeAsync(denied);
        Assert.Equal(429, denied.Response.StatusCode);
        Assert.Equal("60", denied.Response.Headers.RetryAfter.ToString());
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(200, await Hit(limiter, "192.0.2.1"));
    }

    [Fact]
    public async Task Saturation_preserves_active_quota_and_reclaims_only_idle_buckets()
    {
        var time = new Clock();
        var limiter = Limiter(time, 2);
        Assert.Equal(200, await Hit(limiter, "192.0.2.1"));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(200, await Hit(limiter, "192.0.2.2"));
        Assert.Equal(429, await Hit(limiter, "192.0.2.3"));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(200, await Hit(limiter, "192.0.2.3"));
        Assert.Equal(429, await Hit(limiter, "192.0.2.4"));
        Assert.Equal(200, await Hit(limiter, "192.0.2.2"));
    }

    [Fact]
    public async Task Concurrent_admission_and_hits_are_atomic()
    {
        var limiter = Limiter(new Clock(), 1);
        var admissions = await Task.WhenAll(Enumerable.Range(1, 100).Select(i => Task.Run(() => Hit(limiter, $"192.0.2.{i}"))));
        Assert.Equal(1, admissions.Count(x => x == 200));
        var shared = Limiter(new Clock());
        var hits = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => Hit(shared, "192.0.2.1"))));
        Assert.Equal(30, hits.Count(x => x == 200));
    }

    [Fact]
    public async Task Mapped_ipv4_has_same_quota_and_unrelated_routes_bypass()
    {
        var limiter = Limiter(new Clock());
        for (var i = 0; i < 30; i++) await Hit(limiter, "192.0.2.1");
        Assert.Equal(429, await Hit(limiter, "::ffff:192.0.2.1"));
        var context = Context("192.0.2.1");
        context.Request.Path = "/api/v1/me/shifts";
        await limiter.InvokeAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(false, "192.0.2.99", "198.51.100.1", "192.0.2.99")]
    [InlineData(true, "192.0.2.99", "198.51.100.1", "192.0.2.99")]
    [InlineData(true, "10.0.0.1", "198.51.100.1", "198.51.100.1")]
    [InlineData(true, "10.0.0.1", "198.51.100.2, 198.51.100.1", "198.51.100.1")]
    public async Task Only_explicit_proxy_and_bounded_chain_change_identity(bool trust, string peer, string chain, string expected)
    {
        var context = Context(peer);
        context.Request.Headers["X-Forwarded-For"] = chain;
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.99";
        var options = ProxyOptions(trust);
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);
        Assert.Equal(expected, context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task Two_clients_behind_trusted_proxy_have_independent_quotas()
    {
        var limiter = Limiter(new Clock());
        var proxy = new ForwardedHeadersMiddleware(limiter.InvokeAsync, NullLoggerFactory.Instance, Options.Create(ProxyOptions(true)));
        for (var i = 0; i < 31; i++)
        {
            var context = Context("10.0.0.1");
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.1";
            await proxy.Invoke(context);
            Assert.Equal(i < 30 ? 200 : 429, context.Response.StatusCode);
        }
        var other = Context("10.0.0.1");
        other.Request.Headers["X-Forwarded-For"] = "198.51.100.2";
        await proxy.Invoke(other);
        Assert.Equal(200, other.Response.StatusCode);
    }

    [Theory]
    [InlineData("ReverseProxy:ForwardLimit", "0")]
    [InlineData("ReverseProxy:KnownProxies:0", "invalid")]
    [InlineData("ReverseProxy:KnownNetworks:0", "invalid")]
    [InlineData("ReverseProxy:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("ReverseProxy:KnownNetworks:0", "::/0")]
    public void Invalid_proxy_configuration_fails_closed(string key, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => TrustedProxyConfiguration.Configure(new ForwardedHeadersOptions(), config));
    }

    [Theory]
    [InlineData("KnownProxiesCsv", "invalid")]
    [InlineData("KnownProxiesCsv", "192.0.2.1,")]
    [InlineData("KnownProxiesCsv", ",192.0.2.1")]
    [InlineData("KnownProxiesCsv", "192.0.2.1,,192.0.2.2")]
    [InlineData("KnownProxiesCsv", " ")]
    [InlineData("KnownNetworksCsv", "invalid")]
    [InlineData("KnownNetworksCsv", "0.0.0.0/0")]
    [InlineData("KnownNetworksCsv", "::/0")]
    [InlineData("KnownNetworksCsv", "192.0.2.0/24,")]
    public void Invalid_csv_fails_closed(string name, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ReverseProxy:" + name] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => TrustedProxyConfiguration.Configure(new ForwardedHeadersOptions(), config));
    }

    [Theory]
    [InlineData("KnownProxies", "192.0.2.1")]
    [InlineData("KnownNetworks", "192.0.2.0/24")]
    public void Nonempty_array_and_csv_are_rejected_as_ambiguous(string name, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ReverseProxy:" + name + ":0"] = value, ["ReverseProxy:" + name + "Csv"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => TrustedProxyConfiguration.Configure(new ForwardedHeadersOptions(), config));
    }

    [Fact]
    public void Compose_environment_csv_binds_explicit_peers_hops_and_capacity()
    {
        const string prefix = "CLEANINGSUITE_PROXY_FIXTURE_";
        var values = new Dictionary<string, string> {
            ["ReverseProxy__KnownProxiesCsv"] = "192.0.2.1, ::ffff:192.0.2.2",
            ["ReverseProxy__KnownNetworksCsv"] = "198.51.100.0/24, 2001:db8::/32",
            ["ReverseProxy__ForwardLimit"] = "2", ["PublicRateLimit__Capacity"] = "123" };
        try
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(prefix + pair.Key, pair.Value);
            var config = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var options = new ForwardedHeadersOptions();
            TrustedProxyConfiguration.Configure(options, config);
            Assert.Equal(2, options.ForwardLimit);
            Assert.Equal(new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2") }, options.KnownProxies);
            Assert.Equal(2, options.KnownIPNetworks.Count);
            Assert.Equal(123, config.GetSection("PublicRateLimit").Get<PublicRateLimitOptions>()!.Capacity);
            Assert.NotEqual(ForwardedHeaders.None, options.ForwardedHeaders);
        }
        finally
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(prefix + pair.Key, null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_csv_disables_forwarding_unless_explicit_array_is_configured(bool array)
    {
        var values = new Dictionary<string, string?> { ["ReverseProxy:KnownProxiesCsv"] = "", ["ReverseProxy:KnownNetworksCsv"] = "" };
        if (array) values["ReverseProxy:KnownProxies:0"] = "192.0.2.1";
        var options = new ForwardedHeadersOptions();
        TrustedProxyConfiguration.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Equal(array ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(array ? 1 : 0, options.KnownProxies.Count);
    }

    private static ForwardedHeadersOptions ProxyOptions(bool trust)
    {
        var values = new Dictionary<string, string?>();
        if (trust) values["ReverseProxy:KnownProxies:0"] = "10.0.0.1";
        var options = new ForwardedHeadersOptions();
        TrustedProxyConfiguration.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return options;
    }
    private static PublicRateLimitMiddleware Limiter(Clock time, int capacity = 100) =>
        new(_ => Task.CompletedTask, time, Options.Create(new PublicRateLimitOptions { Capacity = capacity }));
    private static DefaultHttpContext Context(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Path = "/api/v1/public/bookings";
        return context;
    }
    private static async Task<int> Hit(PublicRateLimitMiddleware limiter, string ip)
    {
        var context = Context(ip);
        await limiter.InvokeAsync(context);
        return context.Response.StatusCode;
    }
    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }
}
