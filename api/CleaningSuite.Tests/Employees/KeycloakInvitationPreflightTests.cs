using System.Net;
using CleaningSuite.Application.Employees;
using CleaningSuite.Infrastructure.Keycloak;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Tests.Employees;

public class KeycloakInvitationPreflightTests
{
    [Theory]
    [InlineData("valid")][InlineData("interaction")]
    public async Task PublicProbeRequiresExactUnauthenticatedAuthorizationError(string mode)
    {
        using var http = new HttpClient(new Probe(mode));
        await new KeycloakInvitationPreflight(http).ValidateAsync(Config(), "tenant-a", Landing, default);
    }

    [Theory]
    [InlineData("state")][InlineData("duplicate-state")][InlineData("origin")][InlineData("path")]
    [InlineData("query")][InlineData("fragment")][InlineData("code")][InlineData("token")]
    [InlineData("issuer")][InlineData("status")][InlineData("missing-location")][InlineData("relative")]
    [InlineData("error")][InlineData("missing-client")][InlineData("disabled-client")][InlineData("unregistered")]
    public async Task InvalidOrUnregisteredAuthorizationResponsesFailClosed(string mode)
    {
        using var http = new HttpClient(new Probe(mode));
        await Assert.ThrowsAsync<EmployeeInviteConfigurationException>(() => new KeycloakInvitationPreflight(http).ValidateAsync(Config(), "tenant-a", Landing, default));
    }

    private const string Landing = "https://site.invalid/account/?tenant=tenant-a";
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Auth:Keycloak:ServerUrl"] = "https://public.invalid", ["Auth:Keycloak:AdminServerUrl"] = "http://internal.invalid"
    }).Build();
    private sealed class Probe(string mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization); Assert.Null(request.Headers.UserAgent.FirstOrDefault());
            Assert.Equal("internal.invalid", request.RequestUri!.Host);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal("none", query["prompt"]); Assert.Equal("S256", query["code_challenge_method"]);
            Assert.Equal(43, query["code_challenge"].ToString().Length);
            Assert.Equal("code", query["response_type"]); Assert.Equal(Landing, query["redirect_uri"]);
            var state = query["state"].ToString(); Assert.Equal(43, state.Length);
            var location = Landing + "&error=" + (mode == "interaction" ? "interaction_required" : "login_required") + "&state=" + state + "&iss=https%3A%2F%2Fpublic.invalid%2Frealms%2Ftenant-a";
            location = mode switch {
                "state" => location.Replace(state, "wrong"), "duplicate-state" => location + "&state=" + state,
                "origin" => location.Replace("site.invalid", "evil.invalid"), "path" => location.Replace("/account/", "/else/"),
                "query" => location.Replace("tenant=tenant-a", "tenant=other"), "fragment" => location + "#token",
                "code" => location + "&code=unexpected", "token" => location + "&access_token=unexpected",
                "issuer" => location.Replace("public.invalid", "other.invalid"), "relative" => "/account/?error=login_required&state=" + state,
                "error" => location.Replace("login_required", "invalid_request"), _ => location
            };
            var status = mode is "status" or "missing-client" or "disabled-client" or "unregistered" ? HttpStatusCode.BadRequest : HttpStatusCode.Redirect;
            var response = new HttpResponseMessage(status);
            if (mode != "missing-location") response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        }
    }
}
