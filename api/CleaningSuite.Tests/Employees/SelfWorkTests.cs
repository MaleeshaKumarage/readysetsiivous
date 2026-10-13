using System.Text.Json;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using Moq;

namespace CleaningSuite.Tests.Employees;

public class SelfWorkTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void SelfProfileHasExactAllowlistAndNoPayrollHrOrIdentityInternals()
    {
        var e = new Employee { PayRate = 9876.54m, Notes = "secret-hr-note", KeycloakUserId = "secret-subject",
            InviteError = "secret-delivery-error", Certifications = new() { new() { Name = "secret-certification" } } };
        var json = JsonSerializer.Serialize(SelfResponses.Employee(e), Json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "colorHex", "defaultHours", "email", "firstName", "id", "isActive", "lastName", "phone", "role", "serviceAreas", "skills" },
            document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("9876.54", json);
    }

    [Fact]
    public void OperationalBookingHasExactNestedAllowlistsWithoutFinancialOrCapabilitySecrets()
    {
        var b = new Booking { Notes = "secret-note", CustomerReference = "secret-lookup", Source = "secret-source",
            Customer = new() { Name = "Customer", Phone = "Phone", Email = "secret-email" },
            Service = new() { NameFi = "Clean", PriceNet = 7654.32m, VatRatePercent = 123.45m },
            History = new() { new() { By = "secret-actor", Note = "secret-history" } } };
        var json = JsonSerializer.Serialize(SelfResponses.Booking(b), Json);
        using var d = JsonDocument.Parse(json);
        Assert.Equal(new[] { "bookingNumber", "cleaningAddress", "customer", "endUtc", "id", "service", "startLocalDate", "startLocalTime", "startUtc", "status" },
            d.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(new[] { "name", "phone" }, d.RootElement.GetProperty("customer").EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(new[] { "durationMinutes", "nameEn", "nameFi", "nameSv" }, d.RootElement.GetProperty("service").EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.DoesNotContain("secret", json); Assert.DoesNotContain("7654.32", json); Assert.DoesNotContain("123.45", json);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task BookingDetailChecksActualOwnerEvenWhenRepositoryReturnsAnObject(bool own)
    {
        var employee = Guid.NewGuid(); var b = new Booking { EmployeeId = (own ? employee : Guid.NewGuid()).ToString() };
        var repo = new Mock<IBookingRepository>(); repo.Setup(r => r.GetByIdAsync(b.Id, It.IsAny<CancellationToken>())).ReturnsAsync(b);
        var result = await new GetOwnBookingHandler(repo.Object).Handle(new(employee, b.Id), default);
        Assert.Equal(own, result is not null);
    }

    [Theory]
    [InlineData("2026-02-30")] [InlineData("2026-2-04")] [InlineData("0001-01-01")]
    [InlineData("9999-12-31")] [InlineData("")] [InlineData("2026-10-13T00:00:00Z")]
    public async Task InvalidDayIsRejectedBeforeAnyDataReads(string day)
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Handler.Handle(new(f.Employee, day), default));
        Assert.Empty(f.Profiles.Invocations); Assert.Empty(f.Bookings.Invocations); Assert.Empty(f.Shifts.Invocations);
    }

    [Theory]
    [InlineData("2026-03-29", "2026-03-28T22:00:00Z", "2026-03-29T21:00:00Z")]
    [InlineData("2026-10-25", "2026-10-24T21:00:00Z", "2026-10-25T22:00:00Z")]
    [InlineData("2026-01-01", "2025-12-31T22:00:00Z", "2026-01-01T22:00:00Z")]
    [InlineData("2024-02-29", "2024-02-28T22:00:00Z", "2024-02-29T22:00:00Z")]
    public async Task DayUsesIndependentTenantLocalBoundaryTableAndRechecksOwnership(string day, string start, string end)
    {
        var f = new Fixture();
        var from = DateTime.Parse(start, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var to = DateTime.Parse(end, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var first = new Booking { EmployeeId = f.Employee.ToString(), StartUtc = from };
        f.Bookings.Setup(r => r.ListAsync(from, to, null, f.Employee, It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
            first, new Booking { EmployeeId = f.Employee.ToString(), StartUtc = to },
            new Booking { EmployeeId = Guid.NewGuid().ToString(), StartUtc = from.AddHours(1) },
            new Booking { EmployeeId = f.Employee.ToString(), StartUtc = from.AddTicks(-1) }
        });
        var result = await f.Handler.Handle(new(f.Employee, day), default);
        Assert.Equal(first.Id, Assert.Single(result.Bookings).Id);
        Assert.Empty(result.Shifts); Assert.Equal("Europe/Helsinki", result.TimeZoneId);
        f.Bookings.Verify(r => r.ListAsync(from, to, null, f.Employee, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("during", true)] [InlineData("after", false)] [InlineData("end", false)]
    [InlineData("unassigned", false)] [InlineData("foreign-employee", false)]
    [InlineData("inactive-shift", false)] [InlineData("missing-company", false)]
    [InlineData("foreign-branch-parent", false)] [InlineData("inactive-branch", false)]
    [InlineData("reassignment-gap", false)]
    public async Task ShiftViewRequiresCurrentOwnershipValidParentsAndActualAssignmentOverlap(string scenario, bool visible)
    {
        var f = new Fixture();
        var company = Company.Create("test", "Company", notes: "secret-company-notes");
        var branch = Branch.Create(company.Id, "Branch", "Street");
        var shift = Shift.Create(company.Id, branch.Id, "Late shift", new ShiftSchedule {
            Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(22.5), DailyEnd = TimeSpan.FromHours(23.5)
        }, "secret-shift-notes", null, null);
        var assignment = ShiftAssignment.Create(shift.Id, f.Employee, "secret-assignment-note");
        var start = new DateTime(2026,10,12,22,30,0,DateTimeKind.Utc);
        assignment.AssignedAtUtc = start.AddMinutes(30);
        switch (scenario) {
            case "after": assignment.AssignedAtUtc = start.AddHours(2); break;
            case "end": assignment.AssignedAtUtc = start.AddHours(1); break;
            case "unassigned": assignment.IsActive = false; break;
            case "foreign-employee": assignment.EmployeeId = Guid.NewGuid(); break;
            case "inactive-shift": shift.IsActive = false; break;
            case "foreign-branch-parent": branch.CompanyId = Guid.NewGuid(); break;
            case "inactive-branch": branch.IsActive = false; break;
            case "reassignment-gap": assignment.Periods = new() {
                new() { FromUtc = start.AddDays(-1), ToUtc = start },
                new() { FromUtc = start.AddHours(1) }
            }; break;
        }
        f.Shifts.Setup(r => r.ListAssignmentsByEmployeeAsync(f.Employee, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { assignment });
        f.Shifts.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        f.Companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(scenario == "missing-company" ? null : company);
        f.Branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        var result = await f.Handler.Handle(new(f.Employee, "2026-10-13"), default);
        Assert.Equal(visible ? 1 : 0, result.Shifts.Count);
        if (visible) {
            Assert.Equal(start, result.Shifts[0].StartUtc);
            Assert.Equal("Street", result.Shifts[0].CleaningAddress.Street);
            Assert.DoesNotContain("secret", JsonSerializer.Serialize(result, Json));
        }
    }

    [Theory]
    [InlineData("")] [InlineData(" ")]
    public async Task BlankTimeZoneUsesDocumentedHelsinkiFallback(string zone)
    {
        var f = new Fixture(); f.Profiles.Setup(p => p.GetAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = zone });
        var result = await f.Handler.Handle(new(f.Employee, "2026-10-13"), default);
        Assert.Equal("Europe/Helsinki", result.TimeZoneId);
    }

    [Fact]
    public async Task InvalidTimeZoneCannotCauseBroadOperationalReads()
    {
        var f = new Fixture(); f.Profiles.Setup(p => p.GetAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "invalid-zone" });
        await Assert.ThrowsAsync<ValidationException>(() => f.Handler.Handle(new(f.Employee, "2026-10-13"), default));
        Assert.Empty(f.Bookings.Invocations); Assert.Empty(f.Shifts.Invocations);
    }

    private sealed class Fixture
    {
        public Guid Employee { get; } = Guid.NewGuid();
        public Mock<IBookingRepository> Bookings { get; } = new();
        public Mock<IShiftRepository> Shifts { get; } = new();
        public Mock<ITenantProfileRepository> Profiles { get; } = new();
        public Mock<ICompanyRepository> Companies { get; } = new();
        public Mock<IBranchRepository> Branches { get; } = new();
        public GetOwnWorkHandler Handler { get; }
        public Fixture()
        {
            Profiles.Setup(p => p.GetAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile());
            Bookings.Setup(b => b.ListAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, Employee, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Booking>());
            Shifts.Setup(s => s.ListAssignmentsByEmployeeAsync(Employee, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CleaningSuite.Domain.Shifts.ShiftAssignment>());
            var tenant = new Mock<ITenantContext>(); tenant.SetupGet(t => t.TenantId).Returns("tenant-a");
            Handler = new(Bookings.Object, Shifts.Object, Companies.Object, Branches.Object, Profiles.Object, tenant.Object);
        }
    }
}
