using System.Net;
using CleaningSuite.Infrastructure.Entertainment;

namespace CleaningSuite.Tests.Entertainment;

public class GeminiProviderErrorParserTests
{
    [Theory]
    [InlineData("RATE_LIMIT_EXCEEDED", "", GeminiFailureKind.RateLimited)]
    [InlineData("QUOTA_EXCEEDED", "", GeminiFailureKind.QuotaExceeded)]
    [InlineData("RESOURCE_EXHAUSTED", "You exceeded your current quota", GeminiFailureKind.QuotaExceeded)]
    [InlineData("RESOURCE_EXHAUSTED", "", GeminiFailureKind.UnknownRateLimit)]
    public void Classifies429UsingSpecificStructuredReason(string reason, string message, GeminiFailureKind expected)
    {
        Assert.Equal(expected, GeminiProviderErrorParser.Classify(HttpStatusCode.TooManyRequests, null, reason, message));
    }

    [Fact]
    public void RecognizesDailyQuotaMessageWhenGoogleOmitsReason()
    {
        Assert.Equal(GeminiFailureKind.QuotaExceeded,
            GeminiProviderErrorParser.Classify(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", null, "Daily quota exceeded"));
    }

    [Theory]
    [InlineData("API_KEY_INVALID", "", GeminiFailureKind.InvalidCredential)]
    [InlineData("PERMISSION_DENIED", "Project does not have access", GeminiFailureKind.ProjectAccessDenied)]
    public void DoesNotTreatAmbiguousProjectDenialAsBadKey(string code, string message, GeminiFailureKind expected)
    {
        Assert.Equal(expected, GeminiProviderErrorParser.Classify(HttpStatusCode.Forbidden, code, null, message));
    }

    [Fact]
    public void ParsesStructuredErrorAndRedactsCredentialFromMessage()
    {
        const string key = "secret-value";
        var parsed = GeminiProviderErrorParser.Parse(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"Quota for secret-value\",\"details\":[{\"reason\":\"RATE_LIMIT_EXCEEDED\"}]}}",
            key);

        Assert.Equal("RESOURCE_EXHAUSTED", parsed.Code);
        Assert.Equal("RATE_LIMIT_EXCEEDED", parsed.Reason);
        Assert.Equal(GeminiFailureKind.RateLimited, parsed.Kind);
        Assert.DoesNotContain(key, parsed.Message);
    }

    [Fact]
    public void InvalidErrorBodyFallsBackToConservativeUnknown429()
    {
        var parsed = GeminiProviderErrorParser.Parse(HttpStatusCode.TooManyRequests, "not-json", "secret");
        Assert.Equal(GeminiFailureKind.UnknownRateLimit, parsed.Kind);
        Assert.Null(parsed.Code);
    }
}
