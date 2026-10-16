using System.Net;
using System.Text.Json;

namespace CleaningSuite.Infrastructure.Entertainment;

public enum GeminiFailureKind
{
    RateLimited,
    QuotaExceeded,
    UnknownRateLimit,
    InvalidCredential,
    ProjectAccessDenied,
    InvalidRequest,
    ProviderUnavailable,
    Unknown
}

public sealed record GeminiProviderError(
    string? Code,
    string? Reason,
    string? Message,
    GeminiFailureKind Kind);

public static class GeminiProviderErrorParser
{
    public static GeminiProviderError Parse(HttpStatusCode status, string body, string secret)
    {
        string? code = null;
        string? reason = null;
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                code = Scalar(error, "status") ?? Scalar(error, "code");
                message = Scalar(error, "message");
                if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (Scalar(detail, "reason") is { Length: > 0 } value) { reason = value; break; }
                    }
                }
            }
        }
        catch (JsonException) { }

        code = Sanitize(code, secret, 100);
        reason = Sanitize(reason, secret, 100);
        message = Sanitize(message, secret, 240);

        return new GeminiProviderError(code, reason, message, Classify(status, code, reason, message));
    }

    public static GeminiFailureKind Classify(HttpStatusCode status, string? code, string? reason, string? message)
    {
        if ((int)status == 429)
        {
            var structuredReason = (reason ?? string.Empty).ToUpperInvariant();
            var structuredCode = (code ?? string.Empty).ToUpperInvariant();
            if (ContainsAny(structuredReason, "RATE_LIMIT_EXCEEDED", "TOO_MANY_REQUESTS")) return GeminiFailureKind.RateLimited;
            if (ContainsAny(structuredReason, "QUOTA_EXCEEDED")) return GeminiFailureKind.QuotaExceeded;
            if (ContainsAny(structuredCode, "RATE_LIMIT_EXCEEDED", "TOO_MANY_REQUESTS")) return GeminiFailureKind.RateLimited;
            if (ContainsAny(structuredCode, "QUOTA_EXCEEDED")) return GeminiFailureKind.QuotaExceeded;

            var messageText = (message ?? string.Empty).ToUpperInvariant();
            if (ContainsAny(messageText, "DAILY QUOTA", "DAILY LIMIT", "REQUESTS PER DAY", "PER-DAY QUOTA"))
                return GeminiFailureKind.QuotaExceeded;
            if (ContainsAny(messageText, "RATE LIMIT EXCEEDED", "TOO MANY REQUESTS", "REQUESTS PER MINUTE", "PER-MINUTE LIMIT"))
                return GeminiFailureKind.RateLimited;
            if (ContainsAny(messageText, "QUOTA EXCEEDED", "CURRENT QUOTA"))
                return GeminiFailureKind.QuotaExceeded;
            return GeminiFailureKind.UnknownRateLimit;
        }

        var fields = $"{code} {reason} {message}".ToUpperInvariant();
        if (((int)status is 401 or 403) && ContainsAny(fields, "API_KEY_INVALID", "INVALID_API_KEY", "API KEY NOT VALID", "UNAUTHENTICATED"))
            return GeminiFailureKind.InvalidCredential;
        if ((int)status == 403) return GeminiFailureKind.ProjectAccessDenied;
        if ((int)status is 400 or 404 or 422) return GeminiFailureKind.InvalidRequest;
        if ((int)status == 408 || (int)status >= 500) return GeminiFailureKind.ProviderUnavailable;
        return GeminiFailureKind.Unknown;
    }

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

    private static string? Sanitize(string? value, string secret, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!string.IsNullOrEmpty(secret)) value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        value = string.Concat(value.Where(c => !char.IsControl(c))).Trim();
        return value.Length <= maximum ? value : value[..maximum];
    }

    private static string? Scalar(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}
