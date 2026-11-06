using System.Security.Claims;
using System.Text.Json;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Http;
using Moq;

namespace CleaningSuite.Tests.Companies;

public sealed class PartnerUpdateCompatibilityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"isPartnerEnabled\":null}", true)]
    [InlineData("{\"isPartnerEnabled\":false}", false)]
    [InlineData("{\"isPartnerEnabled\":true}", true)]
    public async Task Company_update_preserves_omitted_partner_flag_but_honors_explicit_boolean(string body, bool expected)
    {
        var company = Company.Create("3404861-9", "Customer"); company.IsPartnerEnabled = true;
        var repository = new Mock<ICompanyRepository>();
        repository.Setup(r => r.GetAsync(company.Id, default)).ReturnsAsync(company);
        var input = JsonSerializer.Deserialize<UpdateCompanyCommand>(body, Json)!
            with { Id = company.Id, BusinessId = company.BusinessId, Name = "Updated customer" };

        var result = await new CompanyHandlers.UpdateCompanyCommandHandler(repository.Object).Handle(input, default);

        Assert.Equal(expected, result.IsPartnerEnabled);
        Assert.Equal("Updated customer", result.Name);
        repository.Verify(r => r.SaveAsync(company, default), Times.Once);
    }

    [Theory]
    [InlineData("{\"name\":\"Renamed property\"}")]
    [InlineData("{\"name\":\"Renamed property\",\"priceNet\":null,\"vatRatePercent\":null,\"estimatedDurationMinutes\":null}")]
    public async Task Legacy_branch_update_preserves_counts_and_existing_price_snapshot(string body)
    {
        var branch = PricedBranch(); var revision = branch.PricingVersion;
        var input = JsonSerializer.Deserialize<UpdateBranchCommand>(body, Json)!;
        var updated = await Update(branch, input);

        Assert.Equal("Renamed property", updated.Name);
        Assert.Equal(3, updated.RoomCount); Assert.Equal(5, updated.BedCount);
        Assert.Equal(150m, updated.PriceNet); Assert.Equal(25.5m, updated.VatRatePercent);
        Assert.Equal(120, updated.EstimatedDurationMinutes); Assert.Equal(revision, updated.PricingVersion);
    }

    [Fact]
    public async Task Explicit_null_clears_only_the_selected_count_and_survives_route_id_copy()
    {
        var branch = PricedBranch();
        var input = JsonSerializer.Deserialize<UpdateBranchCommand>("{\"name\":\"Property\",\"roomCount\":null}", Json)!;
        // Controllers replace the route ID with a record copy; field presence must survive it.
        var updated = await Update(branch, input with { Id = Guid.NewGuid() });

        Assert.Null(updated.RoomCount); Assert.Equal(5, updated.BedCount);
        Assert.Equal(150m, updated.PriceNet); Assert.True(updated.PricingReady);
        Assert.DoesNotContain("hasRoomCount", JsonSerializer.Serialize(input, Json));
        Assert.DoesNotContain("hasBedCount", JsonSerializer.Serialize(input, Json));
    }

    [Fact]
    public async Task New_client_can_update_counts_and_complete_pricing_without_resetting_other_fields()
    {
        var branch = PricedBranch(); var revision = branch.PricingVersion;
        var input = JsonSerializer.Deserialize<UpdateBranchCommand>(
            "{\"name\":\"Property\",\"bedCount\":8,\"priceNet\":175,\"vatRatePercent\":25.5,\"estimatedDurationMinutes\":150}", Json)!;

        var updated = await Update(branch, input);

        Assert.Equal(3, updated.RoomCount); Assert.Equal(8, updated.BedCount);
        Assert.Equal(175m, updated.PriceNet); Assert.Equal(150, updated.EstimatedDurationMinutes);
        Assert.Equal(revision + 1, updated.PricingVersion);
    }

    [Fact]
    public async Task Positional_constructor_and_with_count_updates_remain_supported()
    {
        var branch = PricedBranch();
        await Update(branch, new(branch.Id, "Property", RoomCount: 7));
        Assert.Equal(7, branch.RoomCount); Assert.Equal(5, branch.BedCount);
        await Update(branch, new UpdateBranchCommand(branch.Id, "Property") with { BedCount = null });
        Assert.Equal(7, branch.RoomCount); Assert.Null(branch.BedCount);
    }

    [Fact]
    public async Task Partner_company_change_uses_current_binding_without_changing_identity_permissions()
    {
        var oldCompany = Company.Create("OLD", "Old customer"); oldCompany.IsPartnerEnabled = true;
        var nextCompany = Company.Create("NEXT", "New customer"); nextCompany.IsPartnerEnabled = true;
        var employee = new Employee { Email = "owner@example.invalid", Role = Employee.RolePartner,
            PartnerCompanyId = oldCompany.Id, KeycloakUserId = "owner" };
        var people = new Mock<IEmployeeRepository>();
        people.Setup(r => r.GetByIdAsync(employee.Id, default)).ReturnsAsync(employee);
        people.Setup(r => r.GetByKeycloakUserIdAsync("owner", default)).ReturnsAsync(employee);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(r => r.GetAsync(oldCompany.Id, default)).ReturnsAsync(oldCompany);
        companies.Setup(r => r.GetAsync(nextCompany.Id, default)).ReturnsAsync(nextCompany);
        var identities = new Mock<IEmployeeIdentityProvider>(MockBehavior.Strict);
        var locks = new Mock<IEmployeeIdentityLock>();
        locks.Setup(l => l.AcquireAsync("tenant", employee.Email, default)).ReturnsAsync(new Lease());
        var work = new Mock<IShiftWorkReservation>(); work.Setup(l => l.AcquireAsync(default)).ReturnsAsync(new Lease());
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(t => t.TenantId).Returns("tenant");
        var fields = new EmployeeFields(employee.Email, "Property", "Owner", "", Employee.RolePartner,
            null, new(), null, null, null, null, null, nextCompany.Id);

        await new UpdateEmployeeHandler(people.Object, identities.Object, tenant.Object, locks.Object, work.Object, companies.Object)
            .Handle(new(employee.Id, fields, true), default);

        Assert.Equal(nextCompany.Id, employee.PartnerCompanyId);
        Assert.False(employee.IdentitySyncPending); identities.VerifyNoOtherCalls();
        // The same JWT is scoped by current database membership, never a stale company claim.
        oldCompany.IsPartnerEnabled = false;
        var guard = new EmployeeAccessGuard(people.Object, companies.Object);
        Assert.True(await guard.AllowAsync(Context()));
        nextCompany.IsPartnerEnabled = false;
        Assert.False(await guard.AllowAsync(Context()));
    }

    [Fact]
    public async Task Re_saving_an_unchanged_price_without_a_basis_keeps_the_billing_mode_and_adds_no_version()
    {
        // A monthly-priced property: the form resends the same price, VAT and duration but no basis.
        var branch = Branch.Create(Guid.NewGuid(), "Property");
        branch.SetPricing(150m, 25.5m, 120, "Monthly", new DateOnly(2026, 1, 1));
        var revision = branch.PricingVersion;
        var input = JsonSerializer.Deserialize<UpdateBranchCommand>(
            "{\"name\":\"Renamed\",\"priceNet\":150,\"vatRatePercent\":25.5,\"estimatedDurationMinutes\":120}", Json)!;

        var updated = await Update(branch, input);

        Assert.Equal(revision, updated.PricingVersion);
        var version = Assert.Single(branch.Versions);
        Assert.Equal("Monthly", version.Mode);
    }

    [Fact]
    public async Task A_changed_price_without_a_basis_keeps_the_monthly_mode_of_the_version_in_force()
    {
        var branch = Branch.Create(Guid.NewGuid(), "Property");
        branch.SetPricing(150m, 25.5m, 120, "Monthly", new DateOnly(2026, 1, 1));
        var input = JsonSerializer.Deserialize<UpdateBranchCommand>(
            "{\"name\":\"Property\",\"priceNet\":160,\"vatRatePercent\":25.5,\"estimatedDurationMinutes\":120}", Json)!;

        await Update(branch, input);

        Assert.Equal("Monthly", branch.Versions[^1].Mode);
        Assert.Equal(160m, branch.Versions[^1].PriceNet);
    }

    private static Branch PricedBranch()
    {
        var branch = Branch.Create(Guid.NewGuid(), "Property"); branch.SetPropertyCounts(3, 5); branch.SetPricing(150m, 25.5m, 120);
        return branch;
    }
    private static async Task<BranchDto> Update(Branch branch, UpdateBranchCommand input)
    {
        var repository = new Mock<IBranchRepository>(); repository.Setup(r => r.GetAsync(branch.Id, default)).ReturnsAsync(branch);
        return await new CompanyHandlers.BranchCommands.UpdateBranchCommandHandler(repository.Object)
            .Handle(input with { Id = branch.Id }, default);
    }
    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext(); context.Request.Path = "/api/v1/partner/me"; context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "owner"), new Claim("email_verified", "true"),
            new Claim("realm_access", "{\"roles\":[\"partner\"]}") }, "test"));
        return context;
    }
    private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
