using CleaningSuite.Application.Entertainment;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class JoinEntertainmentGroupValidatorTests
{
    private readonly EntertainmentValidators.JoinEntertainmentGroupCommandValidator _validator = new();

    [Fact]
    public void ValidCommand_Passes()
    {
        var cmd = new JoinEntertainmentGroupCommand("ABC234", "Bob", "B1");
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyJoinKey_Fails()
    {
        var cmd = new JoinEntertainmentGroupCommand("", "Bob", "B1");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.JoinKey);
    }

    [Fact]
    public void EmptyName_Fails()
    {
        var cmd = new JoinEntertainmentGroupCommand("ABC234", "", "B1");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PlayerName);
    }

    [Fact]
    public void InvalidLevel_Fails()
    {
        var cmd = new JoinEntertainmentGroupCommand("ABC234", "Bob", "XX");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Level);
    }
}
