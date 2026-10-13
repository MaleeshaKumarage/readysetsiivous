using System.Net;
using System.Text.Json;
using CleaningSuite.Application.Employees;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Infrastructure.Keycloak;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Tests.Employees;
public class KeycloakEmployeeIdentityProviderTests
{
    [Fact]
    public async Task CompletedAccountReinviteUsesEmptyNativeActionsWithoutChangingCredentials()
    {
        var transport = new FakeKeycloak { Verified = true, PendingPassword = false };
        var provider = Provider(transport);
        await provider.SendSetupEmailAsync("tenant-a", "user-a", "https://site.invalid/account/?tenant=tenant-a", 86400);
        var email = Assert.Single(transport.Requests.Where(r => r.Path.Contains("execute-actions-email")));
        Assert.Equal("[]", email.Body); Assert.Contains("lifespan=86400", email.Path);
        Assert.Contains("redirect_uri=https%3A%2F%2Fsite.invalid%2Faccount%2F%3Ftenant%3Dtenant-a", email.Path);
        Assert.DoesNotContain(transport.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/users/user-a"));
    }
    [Fact]
    public async Task PendingAccountRequiresEmailAndPasswordSetup()
    {
        var transport = new FakeKeycloak();
        await Provider(transport).SendSetupEmailAsync("tenant-a", "user-a", "https://site.invalid/account/?tenant=tenant-a", 86400);
        var email = Assert.Single(transport.Requests.Where(r => r.Path.Contains("execute-actions-email")));
        Assert.Contains("VERIFY_EMAIL", email.Body); Assert.Contains("UPDATE_PASSWORD", email.Body);
    }
    [Fact]
    public async Task OwnedAccountReusePreservesPasswordAndUnrelatedRoles()
    {
        var employee = Employee(); var transport = new FakeKeycloak { Owner = employee.Id.ToString(), Verified = true, PendingPassword = false };
        employee.KeycloakUserId = "user-a";
        var result = await Provider(transport).EnsureAsync("tenant-a", employee);
        Assert.Equal("user-a", result.UserId);
        var update = Assert.Single(transport.Requests.Where(r => r.Method == "PUT" && r.Path.EndsWith("/users/user-a")));
        Assert.DoesNotContain("credentials", update.Body); Assert.DoesNotContain("requiredActions", update.Body);
        Assert.DoesNotContain(transport.Requests, r => r.Method == "DELETE" && r.Body.Contains("unrelated"));
    }
    [Fact]
    public async Task VerifiedUnboundAccountWithMatchingRoleCanBeLinkedWithoutPasswordReset()
    {
        var employee = Employee(); var transport = new FakeKeycloak { Verified = true, PendingPassword = false };
        var result = await Provider(transport).EnsureAsync("tenant-a", employee);
        Assert.Equal("user-a", result.UserId); Assert.Equal(employee.Id.ToString(), transport.Owner);
        Assert.DoesNotContain(transport.Requests, r => r.Body.Contains("credentials"));
    }
    [Theory]
    [InlineData(false, "employee")] [InlineData(true, "admin")]
    public async Task UnverifiedOrConflictingUnboundAccountsAreNotClaimed(bool verified, string role)
    {
        var transport = new FakeKeycloak { Verified = verified, Role = role };
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Provider(transport).EnsureAsync("tenant-a", Employee()));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "PUT" || r.Method == "POST" && !r.Path.Contains("openid-connect/token"));
    }
    [Fact]
    public async Task ForeignOwnershipCannotBeOverwritten()
    {
        var employee = Employee(); employee.KeycloakUserId = "user-a";
        var transport = new FakeKeycloak { Owner = Guid.NewGuid().ToString() };
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Provider(transport).EnsureAsync("tenant-a", employee));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "PUT");
    }
    [Fact]
    public async Task ExistingSmtpPasswordMaskIsNeverSentBackAsNewConfiguration()
    {
        var transport = new FakeKeycloak { FrontendConfigured = false };
        await Provider(transport).ValidateInvitationAsync("tenant-a", "https://site.invalid/account/?tenant=tenant-a", "employee");
        var update = Assert.Single(transport.Requests.Where(r => r.Method == "PUT" && r.Path.EndsWith("/realms/tenant-a")));
        Assert.DoesNotContain("smtpServer", update.Body); Assert.DoesNotContain("********", update.Body);
    }
    [Fact]
    public async Task MissingMailConfigurationFailsBeforeCreatingUser()
    {
        var transport = new FakeKeycloak { HasSmtp = false };
        await Assert.ThrowsAsync<EmployeeInviteConfigurationException>(() => Provider(transport).ValidateInvitationAsync("tenant-a", "https://site.invalid/account/?tenant=tenant-a", "employee"));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("/users"));
    }
    [Fact]
    public async Task UnregisteredRedirectFailsBeforeIdentityMutation()
    {
        var transport = new FakeKeycloak();
        await Assert.ThrowsAsync<EmployeeInviteConfigurationException>(() => Provider(transport).ValidateInvitationAsync("tenant-a", "https://other.invalid/account/?tenant=tenant-a", "employee"));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "PUT");
    }
    [Fact]
    public async Task UserEditableOwnershipAttributeIsRejected()
    {
        var transport = new FakeKeycloak { OwnershipUserEditable = true };
        await Assert.ThrowsAsync<EmployeeInviteConfigurationException>(() => Provider(transport).ValidateInvitationAsync("tenant-a", "https://site.invalid/account/?tenant=tenant-a", "employee"));
    }
    [Fact]
    public async Task DemotionRemovesAdminRoleAndTerminatesSessionsWithoutResettingPassword()
    {
        var transport = new FakeKeycloak { Role = "admin" };
        await Provider(transport).SynchronizeAccessAsync("tenant-a", "user-a", "employee", false);
        Assert.Contains(transport.Requests, r => r.Method == "DELETE" && r.Body.Contains("admin"));
        Assert.Contains(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("/logout"));
        Assert.Contains(transport.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/users/user-a") && JsonDocument.Parse(r.Body).RootElement.GetProperty("enabled").ValueKind == JsonValueKind.False);
        Assert.DoesNotContain(transport.Requests, r => r.Body.Contains("credentials"));
    }
    [Fact]
    public async Task PreparingOwnershipDoesNotGrantAnUnboundAccountAdminAccess()
    {
        var employee = Employee(); employee.Role = "admin";
        var transport = new FakeKeycloak { Verified = true, PendingPassword = false, Role = "" };
        await Provider(transport).EnsureAsync("tenant-a", employee);
        Assert.DoesNotContain(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("role-mappings/realm"));
    }
    private static Employee Employee() => new() { Email = "cleaner@example.invalid", FirstName = "Test", LastName = "Cleaner" };
    [Theory]
    [InlineData("admin", "manager")] [InlineData("manager", "employee")] [InlineData("manager", "admin")]
    public async Task ManagerRoleTransitionsRemoveOtherManagedRolesAndPreserveUnrelatedAccess(string from, string to)
    {
        var transport = new FakeKeycloak { Role = from };
        await Provider(transport).SynchronizeAccessAsync("tenant-a", "user-a", to, true);
        Assert.Contains(transport.Requests, r => r.Method == "DELETE" && r.Path.EndsWith("role-mappings/realm") && r.Body.Contains(from));
        Assert.Contains(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("role-mappings/realm") && r.Body.Contains(to));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "DELETE" && r.Body.Contains("unrelated"));
        Assert.DoesNotContain(transport.Requests, r => r.Body.Contains("credentials"));
        if (to == "manager") Assert.Contains(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("/roles") && r.Body.Contains("manager"));
    }
    [Fact]
    public async Task ExistingManagerRealmRoleConflictIsVerifiedAndDoesNotBreakPromotion()
    {
        var transport = new FakeKeycloak { Role = "employee", RoleCreationFailure = HttpStatusCode.Conflict };
        await Provider(transport).SynchronizeAccessAsync("tenant-a", "user-a", "manager", true);
        Assert.Contains(transport.Requests, r => r.Method == "GET" && r.Path.EndsWith("/roles/manager"));
        Assert.Contains(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("role-mappings/realm") && r.Body.Contains("manager"));
    }
    private static KeycloakEmployeeIdentityProvider Provider(FakeKeycloak transport) {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.invalid" }).Build();
        return new(new KeycloakAdminClient(new HttpClient(transport), "https://auth.invalid", "master", "provisioner", "", "admin", "test"), config);
    }
    private sealed class FakeKeycloak : HttpMessageHandler
    {
        public record Request(string Method, string Path, string Body);
        public List<Request> Requests { get; } = new();
        public string? Owner { get; set; }
        public string Role { get; set; } = "employee";
        public bool Verified { get; set; }
        public bool PendingPassword { get; set; } = true;
        public bool HasSmtp { get; set; } = true;
        public bool FrontendConfigured { get; set; } = true;
        public bool OwnershipUserEditable { get; set; }
        public HttpStatusCode? RoleCreationFailure { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            var path = request.RequestUri!.PathAndQuery; var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new(request.Method.Method, path, body));
            if (path.EndsWith("openid-connect/token")) return Json(new { access_token = "test-admin-token", expires_in = 3600 });
            if (request.Method == HttpMethod.Post && path.EndsWith("/roles") && RoleCreationFailure.HasValue) return new(RoleCreationFailure.Value);
            if (request.Method == HttpMethod.Put && path.EndsWith("/users/user-a")) {
                using var update = JsonDocument.Parse(body);
                if (update.RootElement.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("cleaning_employee_id", out var owner)) Owner = owner[0].GetString();
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method != HttpMethod.Get) return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/users/profile")) return Json(new { attributes = new[] { new { name = "cleaning_employee_id", permissions = new { view = new[] { "admin" }, edit = new[] { OwnershipUserEditable ? "user" : "admin" } } } } });
            if (path.EndsWith("/realms/tenant-a")) return Json(new { enabled = true,
                attributes = FrontendConfigured ? new Dictionary<string, string> { ["frontendUrl"] = "https://auth.invalid" } : new(),
                smtpServer = HasSmtp ? new Dictionary<string, string> { ["host"] = "smtp.invalid", ["from"] = "noreply@example.invalid", ["password"] = "********" } : new() });
            if (path.Contains("/clients?")) return Json(new[] { new { clientId = "cleaning-suite-web", enabled = true, redirectUris = new[] { "https://site.invalid/*" } } });
            if (path.EndsWith("/role-mappings/realm")) return Json(new[] { new { id = Role, name = Role }, new { id = "unrelated", name = "unrelated" } });
            if (path.Contains("/roles/")) return Json(new { id = path.Split('/').Last(), name = path.Split('/').Last() });
            object user = new { id = "user-a", email = "cleaner@example.invalid", enabled = true, emailVerified = Verified,
                attributes = Owner is null ? new Dictionary<string, string[]>() : new Dictionary<string, string[]> { ["cleaning_employee_id"] = new[] { Owner } },
                requiredActions = PendingPassword ? new[] { "UPDATE_PASSWORD" } : Array.Empty<string>() };
            if (path.Contains("/users?")) return Json(new[] { user });
            if (path.EndsWith("/users/user-a")) return Json(user);
            throw new InvalidOperationException("Unexpected test request: " + path);
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
