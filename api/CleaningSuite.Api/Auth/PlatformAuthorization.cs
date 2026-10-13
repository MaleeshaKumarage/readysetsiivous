using System.Security.Claims;
using CleaningSuite.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;

namespace CleaningSuite.Api.Auth;

public static class PlatformAuthorization
{
    public const string PolicyName = "PlatformTenantsManage";
    public const string Permission = "platform.tenants.manage";

    public static void AddPlatformPolicy(this AuthorizationOptions options, IConfiguration configuration)
    {
        var resources = new KeycloakResourceOptions(configuration);
        var server = configuration["Auth:Keycloak:ServerUrl"]!.TrimEnd('/');
        options.AddPolicy(PolicyName, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        {
            try
            {
                return KeycloakJwt.RealmFromIssuer(context.User.FindFirstValue("iss") ?? "", server) == resources.ProvisionerRealm
                    && !string.IsNullOrWhiteSpace(context.User.FindFirstValue("sub"))
                    && context.User.FindAll("aud").Any(c => c.Value == resources.PlatformAudience)
                    && RealmRoleAuthorization.HasRole(context.User, Permission);
            }
            catch (Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException) { return false; }
        }));
    }
}
