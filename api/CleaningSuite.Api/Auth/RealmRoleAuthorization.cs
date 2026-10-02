using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace CleaningSuite.Api.Auth;

/// <summary>
/// Checks Keycloak realm roles. Keycloak embeds roles as JSON in the
/// realm_access claim, so ASP.NET's plain RoleClaimType check cannot match them.
/// </summary>
public static class RealmRoleAuthorization
{
    public const string PolicyPrefix = "RealmRole:";

    public static AuthorizationPolicy Policy(params string[] roles) =>
        new AuthorizationPolicyBuilder()
            .AddRequirements(new RealmRoleRequirement(roles))
            .Build();

    public static void AddRealmRolePolicies(this AuthorizationOptions options, params string[] roles)
    {
        foreach (var role in roles)
            options.AddPolicy(PolicyPrefix + role, Policy(role));
    }

    /// <summary>Policy satisfied by ANY of the given roles (e.g. admin or employee).</summary>
    public static void AddRealmRoleAnyPolicy(this AuthorizationOptions options, string name, params string[] roles) =>
        options.AddPolicy(PolicyPrefix + name, Policy(roles));
}

public class RealmRoleRequirement : IAuthorizationRequirement
{
    public RealmRoleRequirement(params string[] roles) => Roles = roles;
    public string[] Roles { get; }
}

public class RealmRoleHandler : AuthorizationHandler<RealmRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RealmRoleRequirement requirement)
    {
        var realmAccess = context.User.FindFirst("realm_access")?.Value;
        if (realmAccess is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(realmAccess);
                if (doc.RootElement.TryGetProperty("roles", out var roles)
                    && roles.EnumerateArray().Any(r => requirement.Roles.Contains(r.GetString())))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
            catch (JsonException)
            {
                // malformed claim: treat as no roles
            }
        }

        context.Fail();
        return Task.CompletedTask;
    }
}
