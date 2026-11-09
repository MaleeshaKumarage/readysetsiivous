using CleaningSuite.Api;

namespace CleaningSuite.Tests.Employees;

public class ReleaseCutoffParsingTests
{
    [Fact]
    public void Offset_timestamp_is_converted_to_utc()
    {
        Assert.Equal(new DateTime(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc), StartupRepairService.ParseReleaseCutoff("2026-11-10T09:00:00+02:00"));
    }

    [Fact]
    public void Timestamp_without_offset_is_taken_as_utc()
    {
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc), StartupRepairService.ParseReleaseCutoff("2026-11-10T09:00:00"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-13-45T99:00")]
    [InlineData("tomorrow")]
    public void Missing_or_malformed_values_do_not_parse(string? value)
    {
        Assert.Null(StartupRepairService.ParseReleaseCutoff(value));
    }
}
