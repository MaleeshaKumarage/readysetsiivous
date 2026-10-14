using Marten.Newtonsoft;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Infrastructure.Keycloak;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CleaningSuite.Tests.Employees;
public class EmployeeIdentityDatabaseFactAttribute : FactAttribute
{
    public EmployeeIdentityDatabaseFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVITE_TEST_DATABASE"))) Skip = "Set INVITE_TEST_DATABASE to an isolated test database."; }
}
public class EmployeeIdentityKeycloakFactAttribute : FactAttribute
{
    public EmployeeIdentityKeycloakFactAttribute() {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVITE_TEST_KEYCLOAK_URL")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVITE_TEST_DATABASE")))
            Skip = "Set isolated INVITE_TEST_KEYCLOAK_URL and INVITE_TEST_DATABASE.";
    }
}
public class EmployeeIdentityIntegrationTests
{
    [EmployeeIdentityDatabaseFact]
    public async Task IdentityKeyIsUniqueWithinTenantWhileUnboundEmployeesAndOtherTenantsAreAllowed()
    {
        using var store = Store(); await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "identity-" + Guid.NewGuid().ToString("N");
        using (var session = store.LightweightSession(tenant)) {
            session.Store(new Employee { Email = "MixedCase@Example.invalid" }, new Employee { Email = "Other@Example.invalid" }); await session.SaveChangesAsync();
        }
        using (var session = store.LightweightSession(tenant)) { session.Store(new Employee { KeycloakUserId = "same-user" }); await session.SaveChangesAsync(); }
        using (var session = store.LightweightSession(tenant)) {
            session.Store(new Employee { KeycloakUserId = "same-user" });
            var error = await Assert.ThrowsAnyAsync<Exception>(() => session.SaveChangesAsync());
            Assert.True(Duplicate(error), error.GetType().Name);
        }
        using (var session = store.LightweightSession(tenant + "-other")) { session.Store(new Employee { KeycloakUserId = "same-user" }); await session.SaveChangesAsync(); }
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(tenant);
        using (var factory = new TenantSessionFactory(store, context.Object)) {
            Assert.NotNull(await new EmployeeRepository(factory).GetByEmailAsync("mixedcase@example.invalid"));
        }
        var locks = new EmployeeIdentityLock(Configuration());
        await using var first = await locks.AcquireAsync(tenant, "Cleaner@Example.invalid");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locks.AcquireAsync(tenant, " cleaner@example.invalid ", cancellation.Token));
        // Different tenants never block each other on the same email.
        await using var other = await locks.AcquireAsync(tenant + "-other", "Cleaner@example.invalid");
    }
    [EmployeeIdentityKeycloakFact]
    public async Task NativeSetupEmailBindsIdentityRetriesDeliveryAndSerializesParallelInvites()
    {
        using var store = Store(); await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var config = Configuration();
        var url = config["Auth:Keycloak:ServerUrl"]!;
        using var http = new HttpClient();
        var admin = new KeycloakAdminClient(http, url, "master", "cleaning-provisioner", "",
            Environment.GetEnvironmentVariable("INVITE_TEST_ADMIN_USER") ?? "invite-test-admin", Environment.GetEnvironmentVariable("INVITE_TEST_ADMIN_PASSWORD") ?? "invite_test_only_2026");
        var realm = "invite-test-" + Guid.NewGuid().ToString("N")[..8];
        var frontend = config["App:FrontendBaseUrl"]!;
        var provisioner = new KeycloakProvisioner(admin, NullLogger<KeycloakProvisioner>.Instance, new CleaningSuite.Infrastructure.Auth.KeycloakResourceOptions(config));
        await provisioner.ProvisionAsync(realm, "Invitation review", "owner@" + realm + ".invalid", "Test", "Owner", new[] { frontend + "/*" });
        var ownerId = (await admin.GetUserIdByUsernameAsync(realm, "owner@" + realm + ".invalid"))!;
        await admin.UpdateUserAsync(realm, ownerId, new { emailVerified = true, requiredActions = Array.Empty<string>(), credentials = new[] { new { type = "password", value = "InviteOwnerTestOnly2026", temporary = false } } });
        using (var registry = store.LightweightSession("registry")) { registry.Store(new TenantRegistration { Slug = realm, Status = TenantRegistration.StatusActive }); await registry.SaveChangesAsync(); }
        var employee = new Employee { Email = "cleaner@" + realm + ".invalid", FirstName = "Invite", LastName = "Cleaner", Role = "employee" };
        using (var session = store.LightweightSession(realm)) { session.Store(employee); await session.SaveChangesAsync(); }
        var identity = new KeycloakEmployeeIdentityProvider(admin, config);
        var redirect = frontend + "/account/?tenant=" + realm;
        await identity.ValidateInvitationAsync(realm, redirect, "employee");
        // Force a captured SMTP failure; no real recipient or mail provider is used.
        await admin.UpdateRealmAsync(realm, new { smtpServer = Smtp("1") });
        await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => Invite(realm, employee.Id, store, config, identity));
        string boundId;
        using (var query = store.QuerySession(realm)) {
            var bound = (await query.LoadAsync<Employee>(employee.Id))!;
            Assert.NotEmpty(bound.KeycloakUserId); Assert.NotNull(bound.InviteError); Assert.Null(bound.InviteLastSentUtc); boundId = bound.KeycloakUserId;
        }
        await admin.UpdateRealmAsync(realm, new { smtpServer = Smtp("1025") });
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Invite(realm, employee.Id, store, config, identity)));
        Assert.Single(results.Where(x => x.Status == "Sent")); Assert.Equal(3, results.Count(x => x.Status == "AlreadySent"));
        Assert.All(results, result => Assert.Equal(boundId, result.KeycloakUserId));
        var user = (await admin.GetUserAsync(realm, boundId))!.Value;
        Assert.Equal(employee.Id.ToString(), user.GetProperty("attributes").GetProperty("cleaning_employee_id")[0].GetString());
        var roles = (await admin.GetUserRealmRolesAsync(realm, boundId)).EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        Assert.Contains("employee", roles); Assert.DoesNotContain("admin", roles);
        var mailboxUrl = Environment.GetEnvironmentVariable("INVITE_TEST_MAILBOX_URL") ?? "http://127.0.0.1:12525";
        var messages = await http.GetStringAsync(mailboxUrl + "/api/v1/messages");
        using var mailbox = JsonDocument.Parse(messages);
        var mail = mailbox.RootElement.GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("To").EnumerateArray().Any(to => to.GetProperty("Address").GetString() == employee.Email));
        var message = await http.GetStringAsync(mailboxUrl + "/api/v1/message/" + mail.GetProperty("ID").GetString());
        using var body = JsonDocument.Parse(message);
        var html = body.RootElement.GetProperty("HTML").GetString()!;
        var match = Regex.Match(html, "href=\"([^\"]*login-actions/action-token[^\"]*)\"");
        Assert.True(match.Success, "Native action email must contain its secure setup link.");
        var setupUrl = WebUtility.HtmlDecode(match.Groups[1].Value);
        Assert.StartsWith(url + "/realms/" + realm + "/login-actions/action-token?", setupUrl);
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(setupUrl).Query)["key"].ToString();
        var action = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(boundId, action.Subject); Assert.InRange(action.ValidTo, DateTime.UtcNow.AddHours(23.9), DateTime.UtcNow.AddHours(24.1));
        // Allow a later completed-account reinvite in the live browser fixture without sleeping60seconds.
        using (var session = store.LightweightSession(realm)) { var saved = (await session.LoadAsync<Employee>(employee.Id))!; saved.InviteLastSentUtc = DateTime.UtcNow.AddMinutes(-2); session.Store(saved); await session.SaveChangesAsync(); }
        var artifactPath = Environment.GetEnvironmentVariable("INVITE_TEST_CONTEXT_PATH");
        if (!string.IsNullOrWhiteSpace(artifactPath)) {
            var company = Company.Create("QC-LIVE-" + realm, "Live shift company");
            var branch = Branch.Create(company.Id, "Live shift branch");
            var template = QualityCycleTemplate.Create("Daily activities", ["Corridor", "Gym"], companyId: company.Id, branchId: branch.Id);
            var shift = Shift.Create(company.Id, branch.Id, "Live employee shift",
                new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.Zero, DailyEnd = TimeSpan.FromHours(1) }, null, null, null, template.Id);
            var assignment = ShiftAssignment.Create(shift.Id, employee.Id, null);
            assignment.AssignedAtUtc = DateTime.UtcNow.Date.AddDays(-8);
            var secondBranch = Branch.Create(company.Id, "Second tenant branch");
            var secondShift = Shift.Create(company.Id, secondBranch.Id, "Second branch shift",
                new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(1), DailyEnd = TimeSpan.FromHours(2) }, null, null, null);
            var booking = new Booking { EmployeeId = employee.Id.ToString(), BookingNumber = "SELF-LIVE-" + realm, StartUtc = DateTime.UtcNow.Date.AddHours(8), EndUtc = DateTime.UtcNow.Date.AddHours(9),
                StartLocalDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"), StartLocalTime = "11:00", Customer = new() { Name = "Own customer", Phone = "123", Email = "private-customer-email" },
                CustomerReference = "private-reference", Notes = "private-booking-note", Service = new() { NameFi = "Own service", PriceNet = 98765m } };
            var otherBooking = new Booking { EmployeeId = Guid.NewGuid().ToString(), BookingNumber = "OTHER-LIVE-" + realm, StartUtc = booking.StartUtc, EndUtc = booking.EndUtc };
            using (var session = store.LightweightSession(realm)) {
                session.Store(company); session.Store(branch); session.Store(template); session.Store(shift); session.Store(assignment);
                session.Store(secondBranch); session.Store(secondShift);
                session.Store(booking); session.Store(otherBooking);
                await session.SaveChangesAsync();
            }
            var otherRealm = realm + "-other";
            await provisioner.ProvisionAsync(otherRealm, "Other isolated tenant", "owner@" + otherRealm + ".invalid", "Other", "Owner", new[] { frontend + "/*" });
            var otherEmployee = new Employee { Role = Employee.RoleManager, Email = "manager@" + otherRealm + ".invalid", FirstName = "Other", LastName = "Manager" };
            var otherUser = await admin.CreateUserAsync(otherRealm, new { username = otherEmployee.Email, email = otherEmployee.Email,
                firstName = otherEmployee.FirstName, lastName = otherEmployee.LastName, requiredActions = Array.Empty<string>(),
                enabled = true, emailVerified = true, credentials = new[] { new { type = "password", value = "InviteOtherManagerTestOnly2026", temporary = false } },
                attributes = new Dictionary<string, string[]> { ["cleaning_employee_id"] = new[] { otherEmployee.Id.ToString() } } });
            await admin.AssignRealmRoleAsync(otherRealm, otherUser, new object[] { await admin.GetRoleAsync(otherRealm, "manager") });
            otherEmployee.KeycloakUserId = otherUser;
            var otherCompany = Company.Create("OTHER-LIVE-" + realm, "Other private company"); var otherBranch = Branch.Create(otherCompany.Id, "Other private branch");
            var otherShift = Shift.Create(otherCompany.Id, otherBranch.Id, "Other private shift", secondShift.Schedule, null, null, null);
            using (var registry = store.LightweightSession("registry")) { registry.Store(new TenantRegistration { Slug = otherRealm, Status = TenantRegistration.StatusActive }); await registry.SaveChangesAsync(); }
            using (var session = store.LightweightSession(otherRealm)) { session.Store(otherEmployee); session.Store(otherCompany); session.Store(otherBranch); session.Store(otherShift); await session.SaveChangesAsync(); }
            await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(new { realm, employeeId = employee.Id, userId = boundId, email = employee.Email, setupUrl, frontend, keycloakUrl = url, mailboxUrl, ownerEmail = "owner@" + realm + ".invalid", shiftId = shift.Id, branchId = branch.Id, companyId = company.Id, secondBranchId = secondBranch.Id,
                otherRealm, otherManagerEmail = otherEmployee.Email, otherShiftId = otherShift.Id,
                bookingId = booking.Id, otherBookingId = otherBooking.Id, workDate = booking.StartLocalDate }));
        }
    }
    private static async Task<EmployeeInviteResult> Invite(string realm, Guid id, IDocumentStore store, IConfiguration config, IEmployeeIdentityProvider identity) {
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(realm);
        using var session = new TenantSessionFactory(store, context.Object);
        var repository = new EmployeeRepository(session);
        return await new InviteEmployeeHandler(repository, context.Object, identity, new EmployeeIdentityLock(config), config, TimeProvider.System).Handle(new(id), default);
    }
    private static Dictionary<string, string> Smtp(string port) => new() { ["host"] = "invite-review-mail", ["port"] = port, ["from"] = "no-reply@example.invalid", ["auth"] = "false", ["starttls"] = "false", ["ssl"] = "false" };
    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("INVITE_TEST_DATABASE"),
        ["Auth:Keycloak:ServerUrl"] = Environment.GetEnvironmentVariable("INVITE_TEST_KEYCLOAK_URL") ?? "http://127.0.0.1:12581",
        ["Auth:Keycloak:Audience"] = "cleaning-suite-api", ["Auth:Keycloak:PlatformAudience"] = "cleaning-suite-platform-api",
        ["App:FrontendBaseUrl"] = Environment.GetEnvironmentVariable("INVITE_TEST_FRONTEND_URL") ?? "http://127.0.0.1:5757",
        ["EmployeeInvites:Smtp:Host"] = "invite-review-mail", ["EmployeeInvites:Smtp:Port"] = "1025",
        ["EmployeeInvites:Smtp:From"] = "no-reply@example.invalid", ["EmployeeInvites:Smtp:Auth"] = "false", ["EmployeeInvites:Smtp:StartTls"] = "false"
    }).Build();
    private static DocumentStore Store() {
        var options = new StoreOptions(); options.RestoreV8Defaults(); options.UseNewtonsoftForSerialization();
        options.DatabaseSchemaName = "fixture_" + Guid.NewGuid().ToString("N"); options.Connection(Environment.GetEnvironmentVariable("INVITE_TEST_DATABASE")!);
        options.Policies.AllDocumentsAreMultiTenanted(); options.Schema.For<Employee>().Index(x => x.KeycloakUserId).UniqueIndex(Marten.Schema.UniqueIndexType.Computed, "mt_doc_employee_uidx_keycloak_identity_key", Marten.Schema.Indexing.Unique.TenancyScope.PerTenant, x => x.KeycloakIdentityKey!);
        return new DocumentStore(options);
    }
    private static bool Duplicate(Exception error) => error is Npgsql.PostgresException { SqlState: "23505" } || error.InnerException is not null && Duplicate(error.InnerException);
}
