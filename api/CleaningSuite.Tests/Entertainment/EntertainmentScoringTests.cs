using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class EntertainmentScoringTests
{
    [Theory]
    [InlineData(true, 7, 3, 10)]
    [InlineData(true, 7, 1, 8)]
    [InlineData(false, 5, 2, 7)]
    [InlineData(false, 0, 3, 3)]
    [InlineData(false, 0, 0, 0)]
    public void Score_is_sum_of_bounded_meaning_and_finnish_grades(bool correct, int meaning, int finnish, int expected)
    {
        var evaluation = new EntertainmentAnswerEvaluation(correct, "answer", "explanation", [], meaning, finnish);

        Assert.Equal(expected, EntertainmentScoring.ScoreAnswer(evaluation));
    }

    [Theory]
    [InlineData(true, 6, 3)]
    [InlineData(false, 7, 3)]
    [InlineData(false, 8, 0)]
    [InlineData(false, 0, 4)]
    public void Invalid_or_inconsistent_ai_grades_are_rejected(bool correct, int meaning, int finnish)
    {
        var evaluation = new EntertainmentAnswerEvaluation(correct, "answer", "explanation", [], meaning, finnish);

        Assert.Throws<ArgumentException>(() => EntertainmentScoring.ScoreAnswer(evaluation));
    }
}
