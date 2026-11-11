using System.Text.Json;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Tests.Shifts;

/// <summary>
/// The admin page and the API agree on field names. These are the exact bodies the page sends (site/src/lib/adminApi.ts,
/// adminBranchVisits) and the fields it reads back, parsed with the API's JSON settings.
/// </summary>
public class BranchVisitContractTests
{
    // The API uses the web defaults (camelCase out) with case-insensitive property names in.
    private static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [Fact]
    public void The_bodies_the_admin_page_sends_bind_to_the_api_requests()
    {
        var add = JsonSerializer.Deserialize<AddBranchVisit>("""
            {"branchId":"11111111-1111-4111-8111-111111111111","fromDate":"2026-11-21","toDate":"2026-11-25","startTime":"08:00","endTime":"10:00",
             "notes":null,"employeeId":null,"useDefaultEmployee":false}
            """, Api)!;
        Assert.Equal(new DateOnly(2026, 11, 21), add.FromDate); Assert.Equal(new DateOnly(2026, 11, 25), add.ToDate);
        Assert.Equal("08:00", add.StartTime); Assert.Equal("10:00", add.EndTime); Assert.Null(add.EmployeeId); Assert.False(add.UseDefaultEmployee);

        var cleaner = JsonSerializer.Deserialize<SetVisitEmployee>("""{"employeeId":"22222222-2222-4222-8222-222222222222","offerRevision":3}""", Api)!;
        Assert.Equal(3, cleaner.OfferRevision); Assert.NotNull(cleaner.EmployeeId);
        Assert.Null(JsonSerializer.Deserialize<SetDefaultEmployee>("""{"employeeId":null}""", Api)!.EmployeeId);
        // A cleaner answers a visit offer with the same body as a partner offer.
        Assert.Equal(4, JsonSerializer.Deserialize<PartnerAcceptance>("""{"version":4,"plannedStartLocal":null}""", Api)!.Version);
    }

    [Fact]
    public void The_fields_the_pages_read_are_the_names_the_api_writes()
    {
        var start = new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc);
        var visit = new BranchVisitDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "AdminPanel", start, start.AddHours(2), new(2026, 11, 10), new(2026, 11, 10),
            "Notes", null, Guid.NewGuid(), 1, null, BranchVisitService.StatusOffered, "Europe/Helsinki");
        using var written = JsonDocument.Parse(JsonSerializer.Serialize(visit, Api));
        foreach (var name in new[] { "id", "shiftId", "branchId", "number", "source", "startUtc", "endUtc", "fromDate", "toDate", "offeredEmployeeId", "offerRevision", "status" })
            Assert.True(written.RootElement.TryGetProperty(name, out _), name);
        Assert.Equal("2026-11-10", written.RootElement.GetProperty("fromDate").GetString());
        Assert.Equal("Offered", written.RootElement.GetProperty("status").GetString());

        var row = new ShiftOccurrenceDto(start, start.AddHours(2), Guid.NewGuid(), null, ShiftOccurrenceExceptionStatus.Extra)
            { VisitNumber = 3, VisitSource = "AdminPanel", OfferedEmployeeId = Guid.NewGuid(), OfferRevision = 1, IsPartnerBooking = false };
        using var listed = JsonDocument.Parse(JsonSerializer.Serialize(row, Api));
        foreach (var name in new[] { "visitNumber", "visitSource", "offeredEmployeeId", "offerRevision", "isPartnerBooking", "exceptionId", "assigneeIds", "canAssign" })
            Assert.True(listed.RootElement.TryGetProperty(name, out _), name);

        var shift = new ShiftDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, true, null, null)
            { IsBranchMain = true, DefaultEmployeeId = Guid.NewGuid() };
        using var listedShift = JsonDocument.Parse(JsonSerializer.Serialize(shift, Api));
        Assert.True(listedShift.RootElement.GetProperty("isBranchMain").GetBoolean());
        Assert.True(listedShift.RootElement.TryGetProperty("defaultEmployeeId", out _));
    }
}
