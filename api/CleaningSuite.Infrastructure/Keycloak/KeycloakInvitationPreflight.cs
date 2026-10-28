using System.Net;
using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Employees;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Keycloak;

// Public authorization validation never receives an administrative token or browser session.
public sealed class KeycloakInvitationPreflight(HttpClient? transport = null)
{
    private static readonly HttpClient PublicHttp = new(new SocketsHttpHandler {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, MaxResponseHeadersLength = 16
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task ValidateAsync(IConfiguration configuration, string realm, string redirect, CancellationToken ct)
    {
        const string failure = "The account landing URL could not be verified with the public Keycloak web client.";
        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var landing) || landing.Scheme is not ("http" or "https")
            || landing.UserInfo.Length != 0 || landing.Fragment.Length != 0 || redirect.Length > 4096)
            throw new EmployeeInviteConfigurationException(failure);
        var original = QueryHelpers.ParseQuery(landing.Query);
        if (original.Keys.Any(k => new[] { "state", "error", "error_description", "iss", "code", "access_token", "id_token" }.Contains(k, StringComparer.OrdinalIgnoreCase)))
            throw new EmployeeInviteConfigurationException(failure);
        var publicIssuer = configuration["Auth:Keycloak:ServerUrl"]!.TrimEnd('/') + "/realms/" + Uri.EscapeDataString(realm);
        var server = (configuration["Auth:Keycloak:AdminServerUrl"] ?? configuration["Auth:Keycloak:ServerUrl"])!.TrimEnd('/');
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var endpoint = QueryHelpers.AddQueryString(server + "/realms/" + Uri.EscapeDataString(realm) + "/protocol/openid-connect/auth",
            new Dictionary<string, string?> { ["client_id"] = "cleaning-suite-web", ["redirect_uri"] = redirect,
                ["response_type"] = "code", ["response_mode"] = "query", ["scope"] = "openid", ["prompt"] = "none", ["state"] = state,
                ["code_challenge_method"] = "S256", ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            // Headers-only: an invalid client can return an HTML error, which we never buffer.
            using var response = await (transport ?? PublicHttp).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var location = response.Headers.Location;
            if (response.StatusCode != HttpStatusCode.Redirect || location is null || !location.IsAbsoluteUri
                || location.OriginalString.Length > 8192 || location.UserInfo.Length != 0 || location.Fragment.Length != 0
                || location.GetLeftPart(UriPartial.Path) != landing.GetLeftPart(UriPartial.Path))
                throw new EmployeeInviteConfigurationException(failure);
            var result = QueryHelpers.ParseQuery(location.Query);
            if (!result.TryGetValue("state", out var returnedState) || returnedState.Count != 1 || returnedState[0] != state
                || !result.TryGetValue("error", out var error) || error.Count != 1 || error[0] is not ("login_required" or "interaction_required")
                || result.Keys.Any(k => !original.ContainsKey(k) && k is not ("state" or "error" or "iss"))
                || original.Any(pair => !result.TryGetValue(pair.Key, out var value) || !pair.Value.SequenceEqual(value))
                || result.TryGetValue("iss", out var issuer) && (issuer.Count != 1 || issuer[0] != publicIssuer))
                throw new EmployeeInviteConfigurationException(failure);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new EmployeeInviteConfigurationException(failure); }
        catch (HttpRequestException) { throw new EmployeeInviteConfigurationException(failure); }
    }
}
