using System.Security.Cryptography;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using WebPush;

namespace CleaningSuite.Tests.Notifications;

public class PushCoreTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/token", true)]
    [InlineData("https://web.push.apple.com/token", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/token", true)]
    [InlineData("https://evilpush.apple.com/token", false)]
    [InlineData("https://push.apple.com.evil.test/token", false)]
    [InlineData("https://push.apple.com/token", false)]
    [InlineData("https://user@fcm.googleapis.com/token", false)]
    [InlineData("https://fcm.googleapis.com:8443/token", false)]
    [InlineData("http://fcm.googleapis.com/token", false)]
    [InlineData("https://fcm.googleapis.com/token#secret", false)]
    [InlineData("https://127.0.0.1/token", false)]
    [InlineData("https://localhost/token", false)]
    [InlineData("https://fcm.googleapis.com.evil.test/token", false)]
    public void Provider_boundary_rejects_SSRF_endpoints(string endpoint, bool expected) => Assert.Equal(expected, PushValidation.Endpoint(endpoint));

    [Fact]
    public void Subscription_keys_require_real_P256_point_and_16_byte_secret()
    {
        var keys = VapidHelper.GenerateVapidKeys();
        var auth = Encode(RandomNumberGenerator.GetBytes(16));
        Assert.True(PushValidation.Keys(new(keys.PublicKey, auth)));
        Assert.False(PushValidation.Keys(new(Encode(new byte[65]), auth)));
        Assert.False(PushValidation.Keys(new(keys.PublicKey, Encode(new byte[15]))));
        Assert.False(PushValidation.Keys(new(keys.PublicKey + "=", auth)));
    }

    [Fact]
    public void Settings_require_matching_key_pair_and_explicit_enable()
    {
        var first = VapidHelper.GenerateVapidKeys(); var second = VapidHelper.GenerateVapidKeys();
        PushSettings Settings(bool enabled, string privateKey) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["PushNotifications:Enabled"] = enabled.ToString(), ["PushNotifications:VapidSubject"] = "mailto:ops@example.test",
            ["PushNotifications:VapidPublicKey"] = first.PublicKey, ["PushNotifications:VapidPrivateKey"] = privateKey }).Build());
        Assert.True(Settings(true, first.PrivateKey).Available);
        Assert.False(Settings(false, first.PrivateKey).Available);
        Assert.False(Settings(true, second.PrivateKey).Available);
        Assert.False(Settings(true, "malformed").Available);
    }
    [Fact]
    public void Public_response_contains_no_credentials()
    {
        var response = System.Text.Json.JsonSerializer.Serialize(new PushConfiguration(true, "public", [new(Guid.NewGuid(), true)]));
        Assert.DoesNotContain("Endpoint", response); Assert.DoesNotContain("Auth", response); Assert.DoesNotContain("Private", response);
    }
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
