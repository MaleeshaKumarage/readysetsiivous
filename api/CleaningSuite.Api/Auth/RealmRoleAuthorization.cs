using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace CleaningSuite.Api.Auth;

/// <summary>
/// Checks Keycloak realm roles. Keycloak embeds roles as JSON in the
/// realm_access claim, so ASP.NET's plain RoleClaimType check cannot match them.
/// </summary>
public static class RealmRoleAuthorization
{
    public static bool HasRole(System.Security.Claims.ClaimsPrincipal user, params string[] expected)
    {
        var claim = user.FindFirst("realm_access")?.Value;
        if (claim is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(claim);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("roles", out var roles)
                && roles.ValueKind == JsonValueKind.Array
                && roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && expected.Contains(r.GetString()));
        }
        catch (JsonException) { return false; }
    }
    public const string PolicyPrefix = "RealmRole:";

    public static AuthorizationPolicy Policy(params string[] roles) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
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
        if (RealmRoleAuthorization.HasRole(context.User, requirement.Roles))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail();
        return Task.CompletedTask;
    }
}
