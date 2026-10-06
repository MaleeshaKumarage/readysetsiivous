using System;
using System.Collections.Generic;
using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftDomainTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    private ShiftSchedule ValidDailySchedule() => new()
    {
        Type = ShiftScheduleType.DailySameTime,
        DailyStart = new TimeSpan(8, 0, 0),
        DailyEnd = new TimeSpan(16, 0, 0)
    };

    [Fact]
    public void Shift_Create_Succeeds_WithValidParameters()
    {
        var validFrom = DateTime.UtcNow.Date;
        var validUntil = validFrom.AddDays(30);

        var shift = Shift.Create(
            _companyId,
            _branchId,
            " Morning Shift ",
            ValidDailySchedule(),
            " Note ",
            validFrom,
            validUntil);

        Assert.NotEqual(Guid.Empty, shift.Id);
        Assert.Equal(_companyId, shift.CompanyId);
        Assert.Equal(_branchId, shift.BranchId);
        Assert.Equal("Morning Shift", shift.Name);
        Assert.Equal("Note", shift.Notes);
        Assert.True(shift.IsActive);
        Assert.Equal(validFrom, shift.ValidFrom);
        Assert.Equal(validUntil, shift.ValidUntil);
    }

    [Fact]
    public void Shift_Create_Throws_WhenCompanyIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => Shift.Create(
            Guid.Empty, _branchId, "Shift", ValidDailySchedule(), null, null, null));
    }

    [Fact]
    public void Shift_Create_Throws_WhenBranchIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => Shift.Create(
            _companyId, Guid.Empty, "Shift", ValidDailySchedule(), null, null, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Shift_Create_Throws_WhenNameIsMissing(string? invalidName)
    {
        Assert.Throws<ArgumentException>(() => Shift.Create(
            _companyId, _branchId, invalidName!, ValidDailySchedule(), null, null, null));
    }

    [Fact]
    public void Shift_Create_Throws_WhenScheduleIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Shift.Create(
            _companyId, _branchId, "Shift", null!, null, null, null));
    }

    [Fact]
    public void Shift_Create_Throws_WhenValidUntilIsBeforeValidFrom()
    {
        var validFrom = DateTime.UtcNow.Date;
        var validUntil = validFrom.AddDays(-1);

        Assert.Throws<ArgumentException>(() => Shift.Create(
            _companyId, _branchId, "Shift", ValidDailySchedule(), null, validFrom, validUntil));
    }

    [Fact]
    public void Shift_Update_Succeeds_And_UpdatesFields()
    {
        var shift = Shift.Create(_companyId, _branchId, "Old Name", ValidDailySchedule(), null, null, null);
        var newCompany = Guid.NewGuid();
        var newBranch = Guid.NewGuid();

        shift.Update(newCompany, newBranch, " Updated Shift ", ValidDailySchedule(), " New Notes ", false, null, null);

        Assert.Equal(newCompany, shift.CompanyId);
        Assert.Equal(newBranch, shift.BranchId);
        Assert.Equal("Updated Shift", shift.Name);
        Assert.Equal("New Notes", shift.Notes);
        Assert.False(shift.IsActive);
    }

    [Fact]
    public void Shift_Update_Throws_OnInvalidParameters()
    {
        var shift = Shift.Create(_companyId, _branchId, "Shift", ValidDailySchedule(), null, null, null);

        Assert.Throws<ArgumentException>(() => shift.Update(Guid.Empty, _branchId, "Shift", ValidDailySchedule(), null, true, null, null));
        Assert.Throws<ArgumentException>(() => shift.Update(_companyId, Guid.Empty, "Shift", ValidDailySchedule(), null, true, null, null));
        Assert.Throws<ArgumentException>(() => shift.Update(_companyId, _branchId, "", ValidDailySchedule(), null, true, null, null));
        Assert.Throws<ArgumentNullException>(() => shift.Update(_companyId, _branchId, "Shift", null!, null, true, null, null));

        var from = DateTime.UtcNow.Date;
        var until = from.AddDays(-5);
        Assert.Throws<ArgumentException>(() => shift.Update(_companyId, _branchId, "Shift", ValidDailySchedule(), null, true, from, until));
    }

    [Fact]
    public void Shift_Deactivate_SetsIsActiveToFalse()
    {
        var shift = Shift.Create(_companyId, _branchId, "Shift", ValidDailySchedule(), null, null, null);
        Assert.True(shift.IsActive);

        shift.Deactivate();

        Assert.False(shift.IsActive);
    }

    [Fact]
    public void ShiftAssignment_Create_Succeeds()
    {
        var shiftId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        var assignment = ShiftAssignment.Create(shiftId, empId, " Primary assignment ");

        Assert.NotEqual(Guid.Empty, assignment.Id);
        Assert.Equal(shiftId, assignment.ShiftId);
        Assert.Equal(empId, assignment.EmployeeId);
        Assert.Equal("Primary assignment", assignment.Note);
        Assert.True(assignment.IsActive);
    }

    [Fact]
    public void ShiftAssignment_Create_Throws_WhenShiftIdOrEmployeeIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => ShiftAssignment.Create(Guid.Empty, Guid.NewGuid(), null));
        Assert.Throws<ArgumentException>(() => ShiftAssignment.Create(Guid.NewGuid(), Guid.Empty, null));
    }

    [Fact]
    public void ShiftSchedule_DailySameTime_Validation()
    {
        // Missing start or end
        var schedule1 = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = null, DailyEnd = new TimeSpan(16, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule1.Validate());

        // End <= Start
        var schedule2 = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(16, 0, 0), DailyEnd = new TimeSpan(8, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule2.Validate());

        // Valid
        var schedule3 = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(8, 0, 0), DailyEnd = new TimeSpan(16, 0, 0) };
        schedule3.Validate();
    }

    [Fact]
    public void ShiftSchedule_DailyDifferentTime_Validation()
    {
        // Empty DailyTimes
        var schedule1 = new ShiftSchedule { Type = ShiftScheduleType.DailyDifferentTime, DailyTimes = new() };
        Assert.Throws<ArgumentException>(() => schedule1.Validate());

        // Null range in dictionary
        var schedule2 = new ShiftSchedule
        {
            Type = ShiftScheduleType.DailyDifferentTime,
            DailyTimes = new() { [DayOfWeek.Monday] = null! }
        };
        Assert.Throws<ArgumentException>(() => schedule2.Validate());

        // Invalid range in dictionary
        var schedule3 = new ShiftSchedule
        {
            Type = ShiftScheduleType.DailyDifferentTime,
            DailyTimes = new() { [DayOfWeek.Monday] = new TimeRange { Start = new TimeSpan(16, 0, 0), End = new TimeSpan(8, 0, 0) } }
        };
        Assert.Throws<ArgumentException>(() => schedule3.Validate());

        // Valid
        var schedule4 = new ShiftSchedule
        {
            Type = ShiftScheduleType.DailyDifferentTime,
            DailyTimes = new() { [DayOfWeek.Monday] = new TimeRange { Start = new TimeSpan(8, 0, 0), End = new TimeSpan(16, 0, 0) } }
        };
        schedule4.Validate();
    }

    [Fact]
    public void ShiftSchedule_Weekly_Validation()
    {
        // Missing days
        var schedule1 = new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyStart = new TimeSpan(8, 0, 0), WeeklyEnd = new TimeSpan(16, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule1.Validate());

        // End <= Start
        var schedule2 = new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDay = DayOfWeek.Monday, WeeklyStart = new TimeSpan(16, 0, 0), WeeklyEnd = new TimeSpan(8, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule2.Validate());

        // Valid with WeeklyDays
        var schedule3 = new ShiftSchedule
        {
            Type = ShiftScheduleType.Weekly,
            WeeklyDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            WeeklyStart = new TimeSpan(8, 0, 0),
            WeeklyEnd = new TimeSpan(16, 0, 0)
        };
        schedule3.Validate();
    }

    [Fact]
    public void ShiftSchedule_BiWeekly_Validation()
    {
        // Missing required field
        var schedule1 = new ShiftSchedule { Type = ShiftScheduleType.BiWeekly, BiWeeklyWeekParity = 1, BiWeeklyDay = DayOfWeek.Monday, BiWeeklyStart = new TimeSpan(8, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule1.Validate());

        // Parity not 1 or 2
        var schedule2 = new ShiftSchedule { Type = ShiftScheduleType.BiWeekly, BiWeeklyWeekParity = 0, BiWeeklyDay = DayOfWeek.Monday, BiWeeklyStart = new TimeSpan(8, 0, 0), BiWeeklyEnd = new TimeSpan(16, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule2.Validate());

        // End <= Start
        var schedule3 = new ShiftSchedule { Type = ShiftScheduleType.BiWeekly, BiWeeklyWeekParity = 1, BiWeeklyDay = DayOfWeek.Monday, BiWeeklyStart = new TimeSpan(16, 0, 0), BiWeeklyEnd = new TimeSpan(8, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule3.Validate());

        // Valid
        var schedule4 = new ShiftSchedule { Type = ShiftScheduleType.BiWeekly, BiWeeklyWeekParity = 2, BiWeeklyDay = DayOfWeek.Monday, BiWeeklyStart = new TimeSpan(8, 0, 0), BiWeeklyEnd = new TimeSpan(16, 0, 0) };
        schedule4.Validate();
    }

    [Fact]
    public void ShiftSchedule_Monthly_Validation()
    {
        // Missing required field
        var schedule1 = new ShiftSchedule { Type = ShiftScheduleType.Monthly, MonthlyDay = 15, MonthlyStart = new TimeSpan(8, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule1.Validate());

        // MonthlyDay out of range (< 1 or > 31)
        var schedule2 = new ShiftSchedule { Type = ShiftScheduleType.Monthly, MonthlyDay = 0, MonthlyStart = new TimeSpan(8, 0, 0), MonthlyEnd = new TimeSpan(16, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule2.Validate());

        var schedule3 = new ShiftSchedule { Type = ShiftScheduleType.Monthly, MonthlyDay = 32, MonthlyStart = new TimeSpan(8, 0, 0), MonthlyEnd = new TimeSpan(16, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule3.Validate());

        // End <= Start
        var schedule4 = new ShiftSchedule { Type = ShiftScheduleType.Monthly, MonthlyDay = 15, MonthlyStart = new TimeSpan(16, 0, 0), MonthlyEnd = new TimeSpan(8, 0, 0) };
        Assert.Throws<ArgumentException>(() => schedule4.Validate());

        // Valid
        var schedule5 = new ShiftSchedule { Type = ShiftScheduleType.Monthly, MonthlyDay = 15, MonthlyStart = new TimeSpan(8, 0, 0), MonthlyEnd = new TimeSpan(16, 0, 0) };
        schedule5.Validate();
    }

    [Fact]
    public void ShiftSchedule_OnCallFlexible_And_UnknownType_Validation()
    {
        var schedule1 = new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible };
        schedule1.Validate();
        Assert.True(schedule1.OnCall);

        var schedule2 = new ShiftSchedule { Type = (ShiftScheduleType)99 };
        Assert.Throws<ArgumentOutOfRangeException>(() => schedule2.Validate());
    }
}
