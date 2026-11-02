using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CleaningSuite.Application.Notifications;
using Microsoft.Extensions.Configuration;
using WebPush;

namespace CleaningSuite.Infrastructure.Notifications;

public static class PushValidation
{
    public static bool Endpoint(string? value)
    {
        if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || IPAddress.TryParse(uri.Host, out _)) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return host == "fcm.googleapis.com" || host == "updates.push.services.mozilla.com" || host == "push.services.mozilla.com"
            || (host.EndsWith(".push.apple.com", StringComparison.Ordinal) && host.Length > ".push.apple.com".Length);
    }
    public static byte[]? Decode(string? value, int length)
    {
        if (value is null || value.Length > 128 || !Regex.IsMatch(value, "^[A-Za-z0-9_-]+$")) return null;
        try { var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); return bytes.Length == length ? bytes : null; }
        catch (FormatException) { return null; }
    }
    public static bool Keys(PushKeys? keys)
    {
        if (keys is null || Decode(keys.Auth, 16) is null || Decode(keys.P256dh, 65) is not { } key || key[0] != 4) return false;
        try { using var ec = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] } }); return true; }
        catch (CryptographicException) { return false; }
    }
    public static Guid Id(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
}

public sealed class PushSettings : IPushSettings
{
    public string PublicKey { get; }
    public string PrivateKey { get; }
    public string Subject { get; }
    public bool Available { get; }
    public string KeyId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PublicKey)));
    public PushSettings(IConfiguration config)
    {
        PublicKey = config["PushNotifications:VapidPublicKey"] ?? "";
        PrivateKey = config["PushNotifications:VapidPrivateKey"] ?? "";
        Subject = config["PushNotifications:VapidSubject"] ?? "";
        if (!config.GetValue<bool>("PushNotifications:Enabled")) return;
        if (PushValidation.Decode(PublicKey, 65) is not { } pub || pub[0] != 4 || PushValidation.Decode(PrivateKey, 32) is not { } secret) return;
        if (!Uri.TryCreate(Subject, UriKind.Absolute, out var subject) || (subject.Scheme != "mailto" && subject.Scheme != "https")) return;
        try
        {
            using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = secret });
            var actual = ec.ExportParameters(false);
            if (!actual.Q.X!.SequenceEqual(pub[1..33]) || !actual.Q.Y!.SequenceEqual(pub[33..65])) return;
            _ = VapidHelper.GetVapidHeaders("https://fcm.googleapis.com", Subject, PublicKey, PrivateKey);
            Available = true;
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException or FormatException) { }
    }
}
