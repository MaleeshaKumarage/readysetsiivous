using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftValidationTests
{
    private readonly ShiftValidators.CreateShiftCommandValidator _createValidator = new();

    [Fact]
    public void ShiftSchedule_WeeklyValidate_Succeeds_WhenWeeklyDayProvided()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.Weekly,
            WeeklyDay = DayOfWeek.Monday,
            WeeklyDays = new List<DayOfWeek> { DayOfWeek.Monday },
            WeeklyStart = new TimeSpan(8, 0, 0),
            WeeklyEnd = new TimeSpan(16, 0, 0)
        };

        var exception = Record.Exception(() => schedule.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void CreateShiftCommandValidator_Succeeds_WhenCommandIsValid()
    {
        var command = new CreateShiftCommand(
            CompanyId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            Name: "Morning Shift",
            Schedule: new ShiftSchedule
            {
                Type = ShiftScheduleType.DailySameTime,
                DailyStart = new TimeSpan(8, 0, 0),
                DailyEnd = new TimeSpan(16, 0, 0)
            }
        );

        var result = _createValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateShiftCommandValidator_Fails_WhenNameIsEmpty()
    {
        var command = new CreateShiftCommand(
            CompanyId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            Name: "",
            Schedule: new ShiftSchedule
            {
                Type = ShiftScheduleType.DailySameTime,
                DailyStart = new TimeSpan(8, 0, 0),
                DailyEnd = new TimeSpan(16, 0, 0)
            }
        );

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }
}
