using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleaningSuite.Api;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Queries;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CleaningSuite.Tests.Auth;

public class EmployeeReadScopeTests
{
    [Theory]
    [InlineData("employee")] [InlineData("manager")] [InlineData("staff")]
    [InlineData("")] [InlineData(null)]
    public async Task SignedNonAdminCannotReadOrMutateAnyAdministrativeFamily(string? role)
    {
        using var host = new Host();
        var id = Guid.NewGuid();
        var operations = new[] {
            ("GET", "employees"), ("GET", "bookings"), ("GET", $"bookings/{id}"),
            ("GET", "invoices"), ("GET", $"invoices/{id}"), ("GET", $"invoices/{id}/pdf"),
            ("GET", "agreements"), ("GET", $"agreements/{id}"), ("GET", $"agreements/{id}/document"),
            ("GET", "services"), ("GET", "tenant"), ("GET", "companies"), ("GET", "shifts"),
            ("GET", $"shifts/{id}/assignments"), ("GET", "schedule?date=2026-10-13&employeeId="+id),
            ("GET", "quality-cycle/templates"),
            ("GET", "quality-cycle/monthly-summary?year=2026&month=10"), ("GET", $"quality-cycle/summary-pdf?shiftId={id}&year=2026&month=10"),
            ("GET", "quality-cycle/report-shares"), ("POST", "quality-cycle/report-shares"), ("DELETE", $"quality-cycle/report-shares/{id}"),
            ("POST", "employees"), ("PUT", $"employees/{id}"), ("POST", $"employees/{id}/invite"),
            ("POST", $"bookings/{id}/confirm"), ("POST", $"invoices/{id}/mark-paid"),
            ("POST", $"agreements/{id}/cancel"), ("DELETE", $"agreements/{id}/signers/{id}"),
            ("PUT", "tenant"), ("POST", "services"), ("POST", "companies"), ("POST", "shifts")
            , ("POST", $"shifts/{id}/reactivate")
        };
        foreach (var (method, path) in operations)
        {
            using var response = await host.Send(method, "/api/v1/admin/"+path, role);
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }
        // Lifecycle/binding lookup is allowed; no payroll, financial, signer,
        // settings or operational handler is allowed to run on a denied request.
        Assert.Empty(host.Mediator.Invocations);
    }

    [Theory]
    [InlineData("employee")] [InlineData("staff")] [InlineData("")] [InlineData(null)]
    public async Task SignedNonOperationsIdentityCannotUseManagerEndpoints(string? role)
    {
        using var host = new Host(); var id = Guid.NewGuid();
        foreach (var (method, path) in new[] { ("GET", "employees"), ("GET", "companies"),
            ("GET", $"companies/{id}/branches"), ("GET", "shifts"), ("GET", $"shifts/{id}"),
            ("POST", "shifts"), ("PUT", $"shifts/{id}"), ("POST", $"shifts/{id}/deactivate"),
            ("POST", $"shifts/{id}/reactivate"),
            ("GET", $"shifts/{id}/assignments"), ("POST", $"shifts/{id}/assignments"), ("DELETE", $"shifts/{id}/assignments/{id}") }) {
            using var response = await host.Send(method, "/api/v1/manager/" + path, role);
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Empty(host.Mediator.Invocations);
        host.Employees.Verify(e => e.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SignedManagerGetsOnlyOperationalRosterFields()
    {
        using var host = new Host(); host.Employee.Role = "manager";
        host.Employee.Email = "secret-email"; host.Employee.Notes = "secret-hr"; host.Employee.PayRate = 98765m;
        host.Employees.Setup(e => e.ListAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { host.Employee });
        using var response = await host.Send("GET", "/api/v1/manager/employees", "manager");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(host.Employee.Id.ToString(), body); Assert.DoesNotContain("secret", body);
        Assert.DoesNotContain("98765", body); Assert.DoesNotContain("scope-sub", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(new[] { "id", "firstName", "lastName", "phone", "isActive", "colorHex", "skills", "serviceAreas", "defaultHours" }.Order(),
            json.RootElement[0].EnumerateObject().Select(p => p.Name).Order());
    }

    [Theory]
    [InlineData("qualityCycleTemplateId")] [InlineData("notes")] [InlineData("companyId")] [InlineData("role")]
    public async Task ManagerCannotSmuggleAdministrativeFieldsIntoShiftEdit(string field)
    {
        using var host = new Host(); host.Employee.Role = "manager";
        var body = JsonSerializer.Serialize(new Dictionary<string, object> { ["name"] = "Shift", ["schedule"] = new { type = 0, dailyStart = "08:00:00", dailyEnd = "10:00:00" }, [field] = Guid.NewGuid() });
        using var response = await host.Send("PUT", "/api/v1/manager/shifts/" + Guid.NewGuid(), "manager", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty(host.Mediator.Invocations);
    }

    [Fact]
    public async Task SignedAdminGetsExplicitAdminContractWithoutComputedIdentityOrVersion()
    {
        using var host = new Host();
        host.Employee.Role = "admin";
        host.Employee.PayRate = 123.45m; host.Employee.Notes = "admin-hr-note";
        host.Mediator.Setup(m => m.Send(It.IsAny<ListEmployeesQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { host.Employee });
        using var response = await host.Send("GET", "/api/v1/admin/employees", "admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin-hr-note", json); Assert.Contains("123.45", json);
        Assert.DoesNotContain("keycloakIdentityKey", json); Assert.DoesNotContain("version", json);
    }

    [Fact]
    public async Task SignedSelfProfileUsesGuardedBindingAndContainsNoHrFields()
    {
        using var host = new Host(); host.Employee.Notes = "secret-hr"; host.Employee.PayRate = 9876.54m;
        using var response = await host.Send("GET", "/api/v1/me/profile?employeeId="+Guid.NewGuid(), "employee");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains(host.Employee.Id.ToString(), json); Assert.DoesNotContain("secret", json); Assert.DoesNotContain("9876.54", json);
        host.Employees.Verify(r => r.GetByKeycloakUserIdAsync("scope-sub", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(host.Mediator.Invocations);
    }

    [Theory]
    [InlineData("admin")] [InlineData("manager")] [InlineData("employee")]
    public async Task EveryStaffRoleUsesItsVerifiedIdentityForOwnShiftClock(string role)
    {
        using var host = new Host(); host.Employee.Role = role;
        var otherEmployee = Guid.NewGuid(); var shift = Guid.NewGuid(); var form = Guid.NewGuid();
        var start = DateTime.UtcNow.Date.AddHours(8); var end = start.AddHours(2);
        var started = new QualityCycleFormDto(form, shift, "Assigned shift", host.Employee.Id, "Staff",
            Guid.NewGuid(), "Activities", start, end, "token", [new("Corridor", false), new("Gym", false)],
            [], null, false, null, start) { StartedAtUtc = start };
        host.Mediator.Setup(m => m.Send(It.IsAny<GetMyShiftsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new MyShiftOccurrenceDto(shift, "Assigned shift", start, end, null) });
        host.Mediator.Setup(m => m.Send(It.IsAny<StartQualityCycleFormCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(started);
        host.Mediator.Setup(m => m.Send(It.IsAny<EndQualityCycleFormCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(started with { IsSubmitted = true, EndedAtUtc = end });
        using var list = await host.Send("GET", "/api/v1/me/shifts?employeeId=" + otherEmployee, role);
        using var began = await host.Send("POST", $"/api/v1/me/shifts/{shift}/start", role,
            JsonSerializer.Serialize(new { occurrenceStartUtc = start, occurrenceEndUtc = end, employeeId = otherEmployee }));
        using var ended = await host.Send("POST", $"/api/v1/me/quality-cycle/{form}/end", role,
            JsonSerializer.Serialize(new { employeeId = otherEmployee, items = new[] { new { itemText = "Corridor", isChecked = true }, new { itemText = "Gym", isChecked = false } } }));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode); Assert.Equal(HttpStatusCode.OK, began.StatusCode); Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        host.Mediator.Verify(m => m.Send(It.Is<GetMyShiftsQuery>(q => q.EmployeeId == host.Employee.Id), It.IsAny<CancellationToken>()), Times.Once);
        host.Mediator.Verify(m => m.Send(It.Is<StartQualityCycleFormCommand>(q => q.EmployeeId == host.Employee.Id && q.ShiftId == shift), It.IsAny<CancellationToken>()), Times.Once);
        host.Mediator.Verify(m => m.Send(It.Is<EndQualityCycleFormCommand>(q => q.EmployeeId == host.Employee.Id && q.FormId == form && q.Items[0].IsChecked && !q.Items[1].IsChecked), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("admin")] [InlineData("manager")] [InlineData("employee")]
    public async Task CalendarUsesOnlyVerifiedEmployeeAndBoundedTenantPeriod(string role)
    {
        using var host = new Host(); host.Employee.Role = role;
        host.Mediator.Setup(m => m.Send(It.IsAny<GetMyShiftsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MyShiftOccurrenceDto>());
        using var response = await host.Send("GET", "/api/v1/me/shifts/calendar?view=future&employeeId=" + Guid.NewGuid(), role);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("occurrences").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("timeZoneId").GetString()));
        host.Mediator.Verify(m => m.Send(It.Is<GetMyShiftsQuery>(q => q.EmployeeId == host.Employee.Id
            && q.FromUtc.Kind == DateTimeKind.Utc && (q.ToUtc - q.FromUtc).TotalDays > 30
            && (q.ToUtc - q.FromUtc).TotalDays < 32), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("admin")] [InlineData("manager")] [InlineData("employee")]
    public async Task ManagementRoleCannotEndAnotherPersonsTask(string role)
    {
        using var host = new Host(); host.Employee.Role = role;
        var otherForm = QualityCycleForm.Create(Guid.NewGuid(), "Other shift", Guid.NewGuid(), "Other staff",
            Guid.NewGuid(), "Activities", DateTime.UtcNow, ["Corridor"], DateTime.UtcNow.AddHours(2));
        otherForm.Start(); var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(r => r.GetFormAsync(otherForm.Id, It.IsAny<CancellationToken>())).ReturnsAsync(otherForm);
        var handler = new QualityCycleHandlers.EndQualityCycleFormCommandHandler(repository.Object, NoopUploadContentValidator.Instance);
        host.Mediator.Setup(m => m.Send(It.IsAny<EndQualityCycleFormCommand>(), It.IsAny<CancellationToken>()))
            .Returns((EndQualityCycleFormCommand command, CancellationToken ct) => handler.Handle(command, ct));
        using var response = await host.Send("POST", $"/api/v1/me/quality-cycle/{otherForm.Id}/end", role,
            JsonSerializer.Serialize(new { employeeId = otherForm.EmployeeId, items = new[] { new { itemText = "Corridor", isChecked = true } } }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(otherForm.IsSubmitted);
        repository.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("admin", "inactive")] [InlineData("manager", "inactive")] [InlineData("employee", "inactive")]
    [InlineData("admin", "pending")] [InlineData("manager", "pending")] [InlineData("employee", "pending")]
    [InlineData("admin", "unverified")] [InlineData("manager", "unverified")] [InlineData("employee", "unverified")]
    [InlineData("admin", "stale-role")] [InlineData("manager", "stale-role")] [InlineData("employee", "stale-role")]
    [InlineData("admin", "unbound")] [InlineData("manager", "unbound")] [InlineData("employee", "unbound")]
    public async Task OwnClockRequiresCurrentActiveLinkedStaffIdentity(string role, string state)
    {
        using var host = new Host(); host.Employee.Role = role;
        host.Employee.IsActive = state != "inactive"; host.Employee.IdentitySyncPending = state == "pending";
        if (state == "stale-role") host.Employee.Role = role == "employee" ? "manager" : "employee";
        if (state == "unbound") host.Employees.Setup(r => r.GetByKeycloakUserIdAsync("scope-sub", It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        var id = Guid.NewGuid();
        foreach (var (method, path, body) in new[] {
            ("GET", "/api/v1/me/shifts", "{}"),
            ("POST", $"/api/v1/me/shifts/{id}/start", JsonSerializer.Serialize(new { occurrenceStartUtc = DateTime.UtcNow, occurrenceEndUtc = DateTime.UtcNow.AddHours(2) })),
            ("POST", $"/api/v1/me/quality-cycle/{id}/end", "{\"items\":[]}") })
        {
            using var response = await host.Send(method, path, role, body, state != "unverified");
            Assert.Equal(state == "unbound" && role == "admin" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Empty(host.Mediator.Invocations);
    }

    private sealed class Host : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        private readonly RsaSecurityKey key;
        private readonly WebApplicationFactory<Program> factory;
        public Mock<IMediator> Mediator { get; } = new();
        public Mock<IEmployeeRepository> Employees { get; } = new();
        public Mock<ITenantProfileRepository> Profiles { get; } = new();
        public Employee Employee { get; } = new() { KeycloakUserId = "scope-sub" };
        private readonly HttpClient client;
        public Host()
        {
            key = new(rsa) { KeyId = "scope-key" }; var pub = rsa.ExportParameters(false);
            var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = key.KeyId, use = "sig", alg = "RS256", n = Base64UrlEncoder.Encode(pub.Modulus!), e = Base64UrlEncoder.Encode(pub.Exponent!) } } });
            Employees.Setup(r => r.GetByKeycloakUserIdAsync("scope-sub", It.IsAny<CancellationToken>())).ReturnsAsync(Employee);
            Profiles.Setup(r => r.GetAsync("tenant-a", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
            var registry = new Mock<ITenantRegistry>(); registry.Setup(r => r.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
                builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> { ["Auth:Keycloak:ServerUrl"]="https://auth.test", ["Auth:Keycloak:Audience"]="cleaning-suite-api" }));
                builder.ConfigureTestServices(services => {
                    foreach (var d in services.Where(d => d.ImplementationType == typeof(MartenSchemaInitializer) || d.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(d);
                    services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(Employees.Object);
                    services.RemoveAll<ITenantProfileRepository>(); services.AddSingleton(Profiles.Object);
                    services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                    services.RemoveAll<IMediator>(); services.AddSingleton(Mediator.Object);
                    services.AddHttpClient("KeycloakSigningKeys").ConfigurePrimaryHttpMessageHandler(() => new Keys(jwks));
                });
            });
            client = factory.CreateClient();
        }
        public Task<HttpResponseMessage> Send(string method, string path, string? role, string body = "{}", bool verified = true)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method != "GET") request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (role is not null) {
                var bound = role is "employee" or "admin" or "manager";
                var jwt = new JwtSecurityToken("https://auth.test/realms/tenant-a", "cleaning-suite-api", new[] {
                    new Claim("sub", bound ? "scope-sub" : "unbound-sub"), new Claim("email_verified",verified ? "true" : "false"),
                    new Claim("realm_access",JsonSerializer.Serialize(new { roles = role=="" ? Array.Empty<string>() : new[] { role } }))
                }, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
                request.Headers.Authorization = new("Bearer",new JwtSecurityTokenHandler().WriteToken(jwt));
            }
            return client.SendAsync(request);
        }
        public void Dispose() { client.Dispose(); factory.Dispose(); rsa.Dispose(); }
    }
    private sealed class Keys(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
