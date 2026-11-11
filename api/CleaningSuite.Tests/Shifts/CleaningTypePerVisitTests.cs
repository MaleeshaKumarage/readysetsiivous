using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

// Cleaning type belongs to each visit, which starts Normal. The property only needs its size to be scheduled.
public class CleaningTypePerVisitTests
{
    private static Branch Property(int? size = 34) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), Name = "Apartment", IsActive = true,
        Street = "Katu 1", PostalCode = "00100", City = "Helsinki", RoomCount = 2, BedCount = 3, SizeM2 = size,
    };

    [Fact]
    public void A_property_with_its_size_can_be_scheduled_without_any_cleaning_type()
    {
        var branch = Property();

        ShiftNaming.EnsureDetails(branch);
        Assert.Equal("Cleaning - Katu 1, 00100 Helsinki |34m2|2H|3V", ShiftNaming.Build(branch));
    }

    [Fact]
    public void A_property_without_its_size_still_cannot_be_scheduled()
    {
        var ex = Assert.Throws<ValidationException>(() => ShiftNaming.EnsureDetails(Property(size: null)));

        Assert.Contains("size", ex.Errors.Single().ErrorMessage);
        Assert.DoesNotContain("cleaning type", ex.Errors.Single().ErrorMessage);
    }

    [Fact]
    public void A_visit_without_a_recorded_type_reads_as_Normal_and_keeps_a_chosen_type()
    {
        var branch = Property();
        var shift = Shift.Create(branch.CompanyId, branch.Id, ShiftNaming.Build(branch),
            new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, null, null);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
        var start = new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc);
        var visit = ShiftOccurrenceException.Create(shift.Id, null, start, start.AddHours(2),
            ShiftOccurrenceExceptionStatus.Extra, "Added");

        Assert.Equal(CleaningTypes.Normal, BranchVisitService.Map(visit, shift, zone, "Europe/Helsinki").CleaningType);

        visit.CleaningType = CleaningTypes.Deep;
        Assert.Equal(CleaningTypes.Deep, BranchVisitService.Map(visit, shift, zone, "Europe/Helsinki").CleaningType);
    }

    [Theory]
    [InlineData(null, "Normal")]
    [InlineData("", "Normal")]
    [InlineData("Normal", "Normal")]
    [InlineData("Deep", "Deep")]
    public void A_missing_or_known_cleaning_type_is_kept_as_Normal_or_Deep(string? value, string expected) =>
        Assert.Equal(expected, CleaningTypes.Normalize(value));

    [Theory]
    [InlineData("Pool")]
    [InlineData("deep")]
    public void Any_other_cleaning_type_is_refused(string value) =>
        Assert.Throws<ArgumentException>(() => CleaningTypes.Normalize(value));
}
