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
        await provider.SendSetupEmailAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "https://site.invalid/account/?tenant=tenant-a", 86400);
        var email = Assert.Single(transport.Requests.Where(r => r.Path.Contains("execute-actions-email")));
        Assert.Equal("[]", email.Body); Assert.Contains("lifespan=86400", email.Path);
        Assert.Contains("redirect_uri=https%3A%2F%2Fsite.invalid%2Faccount%2F%3Ftenant%3Dtenant-a", email.Path);
        Assert.DoesNotContain(transport.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/users/user-a"));
    }
    [Fact]
    public async Task PendingAccountRequiresEmailAndPasswordSetup()
    {
        var transport = new FakeKeycloak();
        await Provider(transport).SendSetupEmailAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "https://site.invalid/account/?tenant=tenant-a", 86400);
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
    public async Task VerifiedUnboundAccountWithMatchingRoleRequiresExplicitReconciliation()
    {
        var employee = Employee(); var transport = new FakeKeycloak { Owner = null, Verified = true, PendingPassword = false };
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Provider(transport).EnsureAsync("tenant-a", employee));
        Assert.Null(transport.Owner);
        Assert.DoesNotContain(transport.Requests, r => r.Method == "PUT");
    }
    [Theory]
    [InlineData(false, "employee")] [InlineData(true, "admin")]
    public async Task UnverifiedOrConflictingUnboundAccountsAreNotClaimed(bool verified, string role)
    {
        var transport = new FakeKeycloak { Owner = null, Verified = verified, Role = role };
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
        await Assert.ThrowsAsync<EmployeeInviteConfigurationException>(() => Provider(transport).ValidateInvitationAsync("tenant-a", "https://site.invalid/account/?tenant=tenant-a", "employee"));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "PUT");
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
        await Provider(transport).SynchronizeAccessAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "employee", false);
        Assert.Contains(transport.Requests, r => r.Method == "DELETE" && r.Path.EndsWith("/groups/admin"));
        Assert.Contains(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("/logout"));
        Assert.Contains(transport.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/users/user-a") && JsonDocument.Parse(r.Body).RootElement.GetProperty("enabled").ValueKind == JsonValueKind.False);
        Assert.DoesNotContain(transport.Requests, r => r.Path.StartsWith("/admin/", StringComparison.Ordinal) && r.Body.Contains("\"credentials\""));
    }
    [Fact]
    public async Task PreparingOwnershipDoesNotGrantAnUnboundAccountAdminAccess()
    {
        var employee = Employee(); employee.Role = "admin";
        var transport = new FakeKeycloak { Owner = null, Verified = true, PendingPassword = false, Role = "" };
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Provider(transport).EnsureAsync("tenant-a", employee));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("role-mappings/realm"));
    }
    [Fact]
    public async Task UpstreamCreateFailureIsExplicitAndDoesNotSendMailOrAdoptPartialState()
    {
        var transport = new FakeKeycloak { MissingUser = true, CreateFailure = HttpStatusCode.InternalServerError };
        var error = await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => Provider(transport).EnsureAsync("tenant-a", Employee()));
        Assert.DoesNotContain("secret-body", error.Message);
        Assert.DoesNotContain(transport.Requests, r => r.Path.Contains("execute-actions-email") || r.Method == "PUT");
        var create = Assert.Single(transport.Requests.Where(r => r.Method == "POST" && r.Path.EndsWith("/users")));
        Assert.Contains("/managed-users", create.Body);
    }
    [Fact]
    public async Task FailedRoleAdditionRequiresDeliberateRetryAndNeverAssignsDirectRoles()
    {
        var transport = new FakeKeycloak { Role = "admin", GroupAddFailure = true };
        var provider = Provider(transport);
        await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => provider.SynchronizeAccessAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "employee", true));
        Assert.Equal("", transport.Role);
        Assert.Contains(transport.Requests, r => r.Path.EndsWith("/logout"));
        Assert.DoesNotContain(transport.Requests, r => r.Path.Contains("execute-actions-email"));
        transport.GroupAddFailure = false;
        await provider.SynchronizeAccessAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "employee", true);
        Assert.Equal("employee", transport.Role);
        Assert.DoesNotContain(transport.Requests, r => r.Method != "GET" && r.Path.Contains("role-mappings"));
        Assert.True(transport.Requests.FindIndex(r => r.Method == "DELETE" && r.Path.EndsWith("/groups/admin"))
            < transport.Requests.FindIndex(r => r.Method == "PUT" && r.Path.EndsWith("/groups/employee")));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task UnmanagedOrUnownedAccountsCannotReceiveSetupMailOrAccessUpdates(bool unmanaged)
    {
        var transport = new FakeKeycloak { Unmanaged = unmanaged, Owner = unmanaged ? Guid.NewGuid().ToString() : null };
        var provider = Provider(transport);
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => provider.SendSetupEmailAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "https://site.invalid", 100));
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => provider.SynchronizeAccessAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "admin", true));
        Assert.DoesNotContain(transport.Requests, r => r.Method is "PUT" or "DELETE" || r.Path.EndsWith("/logout"));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task LegacyDirectRolesOrUnexpectedInheritedAdminFailBeforeMutation(bool direct)
    {
        var transport = new FakeKeycloak { LegacyDirectRole = direct, UnexpectedInheritedAdmin = !direct };
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Provider(transport).SynchronizeAccessAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "manager", true));
        Assert.DoesNotContain(transport.Requests, r => r.Method is "PUT" or "DELETE" || r.Path.EndsWith("/logout"));
    }
    [Fact]
    public async Task CompositeRoleGroupDriftFailsBeforeUserCreation()
    {
        var transport = new FakeKeycloak { MissingUser = true, CompositeGroupRole = true };
        await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => Provider(transport).EnsureAsync("tenant-a", Employee()));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("/users"));
    }
    [Fact]
    public async Task ForeignOwnerCannotBeMutatedByAnyIdentityWriteEntryPoint()
    {
        var transport = new FakeKeycloak();
        var provider = Provider(transport);
        var expected = Guid.NewGuid();
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => provider.AssignRoleAsync("tenant-a", expected, "user-a", "admin"));
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => provider.SendSetupEmailAsync("tenant-a", expected, "user-a", "https://site.invalid", 100));
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => provider.SynchronizeAccessAsync("tenant-a", expected, "user-a", "admin", false));
        Assert.DoesNotContain(transport.Requests, r => r.Method is "PUT" or "DELETE" || r.Path.EndsWith("/logout"));
    }
    [Fact]
    public async Task FailedBestEffortLogoutPreservesOriginalRoleFailure()
    {
        var transport = new FakeKeycloak { Role = "admin", GroupAddFailure = true, LogoutFailure = true };
        var error = await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => Provider(transport).AssignRoleAsync("tenant-a", Guid.Parse(transport.Owner!), "user-a", "employee"));
        Assert.Equal(HttpStatusCode.InternalServerError, Assert.IsType<KeycloakException>(error.InnerException).StatusCode);
        Assert.Contains(transport.Requests, r => r.Path.EndsWith("/logout"));
        Assert.False(transport.LogoutTokenCanceled);
    }
    [Fact]
    public async Task FixedGroupResolutionUsesOnlyFourPathsAndMissingGroupFailsClosed()
    {
        var transport = new FakeKeycloak { MissingFixedGroup = true };
        await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => Provider(transport).EnsureAsync("tenant-a", Employee()));
        Assert.DoesNotContain(transport.Requests, r => r.Path.Contains("/groups?") || r.Method == "PUT" || r.Method == "DELETE");
        Assert.Contains(transport.Requests, r => r.Path.EndsWith("/group-by-path/app-manager"));
        transport.MissingFixedGroup = false;
        transport.Requests.Clear();
        var employee = Employee();
        transport.Owner = employee.Id.ToString();
        await Provider(transport).EnsureAsync("tenant-a", employee);
        var lookups = transport.Requests.Where(r => r.Path.Contains("/group-by-path/")).Select(r => r.Path.Split('/').Last()).ToArray();
        Assert.Equal(new[] { "managed-users", "app-employee", "app-manager", "app-admin" }, lookups);
        Assert.DoesNotContain(transport.Requests, r => r.Path.Contains("/groups?") && !r.Path.Contains("/users/"));
    }
    private static Employee Employee() => new() { Email = "cleaner@example.invalid", FirstName = "Test", LastName = "Cleaner" };
    [Theory]
    [InlineData("admin", "manager")] [InlineData("manager", "employee")] [InlineData("manager", "admin")]
    public async Task ManagerRoleTransitionsRemoveOtherManagedRolesAndPreserveUnrelatedAccess(string from, string to)
    {
        var transport = new FakeKeycloak { Role = from };
        await Provider(transport).SynchronizeAccessAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", to, true);
        Assert.Contains(transport.Requests, r => r.Method == "DELETE" && r.Path.EndsWith("/groups/" + from));
        Assert.Contains(transport.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/groups/" + to));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "DELETE" && r.Body.Contains("unrelated"));
        Assert.DoesNotContain(transport.Requests, r => r.Path.StartsWith("/admin/", StringComparison.Ordinal) && r.Body.Contains("\"credentials\""));
        Assert.DoesNotContain(transport.Requests, r => r.Method == "POST" && r.Path.EndsWith("/roles"));
    }
    [Fact]
    public async Task ManagerPromotionUsesConfiguredRoleGroup()
    {
        var transport = new FakeKeycloak { Role = "employee", RoleCreationFailure = HttpStatusCode.Conflict };
        await Provider(transport).SynchronizeAccessAsync("tenant-a", Guid.Parse(transport.Owner ?? Guid.Empty.ToString()), "user-a", "manager", true);
        Assert.Contains(transport.Requests, r => r.Method == "GET" && r.Path.Contains("/group-by-path/" ));
        Assert.Contains(transport.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/groups/manager"));
    }
    private static KeycloakEmployeeIdentityProvider Provider(FakeKeycloak transport) {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.invalid", ["Auth:Keycloak:TenantAccounts:tenant-a:ClientId"] = "employee-runtime", ["Auth:Keycloak:TenantAccounts:tenant-a:ClientSecret"] = "synthetic-secret" }).Build();
        return new(new TenantKeycloakClients(new HttpClient(transport), config), config, new KeycloakInvitationPreflight(new HttpClient(transport)));
    }
    private sealed class FakeKeycloak : HttpMessageHandler
    {
        public record Request(string Method, string Path, string Body);
        public List<Request> Requests { get; } = new();
        public string? Owner { get; set; } = Guid.NewGuid().ToString();
        public string Role { get; set; } = "employee";
        public bool Verified { get; set; }
        public bool PendingPassword { get; set; } = true;
        public bool HasSmtp { get; set; } = true;
        public bool FrontendConfigured { get; set; } = true;
        public bool OwnershipUserEditable { get; set; }
        public bool MissingFixedGroup { get; set; }
        public bool LegacyDirectRole { get; set; }
        public bool UnexpectedInheritedAdmin { get; set; }
        public bool CompositeGroupRole { get; set; }
        public bool MissingUser { get; set; }
        public bool Unmanaged { get; set; }
        public bool LogoutFailure { get; set; }
        public bool LogoutTokenCanceled { get; set; }
        public bool GroupAddFailure { get; set; }
        public HttpStatusCode? CreateFailure { get; set; }
        public HttpStatusCode? RoleCreationFailure { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            var path = request.RequestUri!.PathAndQuery; var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new(request.Method.Method, path, body));
            if (path.EndsWith("openid-connect/token")) return Json(new { access_token = "test-admin-token", expires_in = 3600 });
            if (path.EndsWith("/logout")) {
                LogoutTokenCanceled = ct.IsCancellationRequested;
                if (LogoutFailure) return new(HttpStatusCode.Forbidden);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/users") && CreateFailure.HasValue) return new(CreateFailure.Value) { Content = new StringContent("secret-body") };
            if (request.Method == HttpMethod.Put && path.Contains("/groups/") && GroupAddFailure) return new(HttpStatusCode.InternalServerError);
            if (request.Method == HttpMethod.Post && path.EndsWith("/roles") && RoleCreationFailure.HasValue) return new(RoleCreationFailure.Value);
            if (request.Method == HttpMethod.Put && path.EndsWith("/users/user-a")) {
                using var update = JsonDocument.Parse(body);
                if (update.RootElement.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("cleaning_employee_id", out var owner)) Owner = owner[0].GetString();
                return new(HttpStatusCode.NoContent);
            }
            if (path.Contains("/users/user-a/groups/") && request.Method == HttpMethod.Put) Role = path.Split('/').Last();
            if (path.Contains("/users/user-a/groups/") && request.Method == HttpMethod.Delete) Role = "";
            if (request.Method != HttpMethod.Get) return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/users/profile")) return Json(new { attributes = new[] { new { name = "cleaning_employee_id", permissions = new { view = new[] { "admin" }, edit = new[] { OwnershipUserEditable ? "user" : "admin" } } } } });
            if (path.EndsWith("/realms/tenant-a/cleaning-identity/v1")) return Json(new { enabled = true,
                attributes = FrontendConfigured ? new Dictionary<string, string> { ["frontendUrl"] = "https://auth.invalid" } : new(),
                smtpServer = HasSmtp ? new Dictionary<string, string> { ["host"] = "smtp.invalid", ["from"] = "noreply@example.invalid", ["password"] = "********" } : new() });
            if (request.RequestUri.AbsolutePath.EndsWith("openid-connect/auth")) {
                Assert.Null(request.Headers.Authorization);
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                var redirect = query["redirect_uri"].ToString();
                if (!redirect.StartsWith("https://site.invalid/", StringComparison.Ordinal)) return new(HttpStatusCode.BadRequest);
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri(redirect + "&error=login_required&state=" + query["state"]);
                return response;
            }
            if (path.Contains("/group-by-path/")) {
                var name = path.Split('/').Last();
                if (MissingFixedGroup && name == "app-manager") return new(HttpStatusCode.NotFound);
                return Json(new { id = name == "managed-users" ? "managed" : name[4..], name, path = "/" + name });
            }
            if (path.Contains("/users/user-a/groups?")) return Json(new[] {new {id=Unmanaged ? "foreign" : "managed",path=Unmanaged ? "/foreign" : "/managed-users"},new {id=Role,path="/app-"+Role}}.Where(g=>g.id != "").ToArray());
            if (path.Contains("/groups/") && path.EndsWith("/role-mappings")) return Json(new { clientMappings = new {} });
            if (path.Contains("/groups/") && path.EndsWith("/role-mappings/realm/composite")) { var group = path.Split("/groups/", 2)[1].Split('/')[0]; return Json(group == "managed" ? Array.Empty<object>() : new object[] {new { id=group, name=group, composite=CompositeGroupRole }}); }
            if (path.EndsWith("/role-mappings/realm/composite")) return Json(new[] { new { id = Role, name = Role }, new { id = UnexpectedInheritedAdmin ? "admin" : "", name = UnexpectedInheritedAdmin ? "admin" : "" } }.Where(r=>r.id != "").ToArray());
            if (path.EndsWith("/role-mappings/realm")) return Json(new[] { new { id = LegacyDirectRole ? Role : "unrelated", name = LegacyDirectRole ? Role : "unrelated" } });
            if (path.Contains("/roles/")) return Json(new { id = path.Split('/').Last(), name = path.Split('/').Last() });
            object user = new { id = "user-a", email = "cleaner@example.invalid", enabled = true, emailVerified = Verified,
                attributes = Owner is null ? new Dictionary<string, string[]>() : new Dictionary<string, string[]> { ["cleaning_employee_id"] = new[] { Owner } },
                requiredActions = PendingPassword ? new[] { "UPDATE_PASSWORD" } : Array.Empty<string>() };
            if (path.Contains("/users?")) return Json(MissingUser ? Array.Empty<object>() : new[] { user });
            if (path.EndsWith("/users/user-a")) return Json(user);
            throw new InvalidOperationException("Unexpected test request: " + path);
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
