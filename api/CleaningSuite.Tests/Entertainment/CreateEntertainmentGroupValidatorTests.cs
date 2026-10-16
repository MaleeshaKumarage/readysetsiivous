using CleaningSuite.Application.Entertainment;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class CreateEntertainmentGroupValidatorTests
{
    private readonly EntertainmentValidators.CreateEntertainmentGroupCommandValidator _validator = new();

    private static CreateEntertainmentGroupCommand ValidCommand() =>
        new(
            Name: "Travel English",
            Description: new string('x', 200),
            CreatorName: "Alice",
            CreatorLevel: "A2");

    [Fact]
    public void ValidCommand_Passes()
    {
        var result = _validator.Validate(ValidCommand());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyName_Fails()
    {
        var cmd = ValidCommand() with { Name = "" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void ShortDescription_Fails()
    {
        var cmd = ValidCommand() with { Description = new string('x', 9) };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void DescriptionAtLeast10_Passes()
    {
        var cmd = ValidCommand() with { Description = new string('x', 200) };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void TooLongDescription_Fails()
    {
        var cmd = ValidCommand() with { Description = new string('x', 2001) };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void InvalidLevel_Fails()
    {
        var cmd = ValidCommand() with { CreatorLevel = "Z9" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.CreatorLevel);
    }

    [Theory]
    [InlineData("a1")]
    [InlineData("A1")]
    [InlineData("C2")]
    public void ValidLevel_Passes(string level)
    {
        var cmd = ValidCommand() with { CreatorLevel = level };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.CreatorLevel);
    }
}
