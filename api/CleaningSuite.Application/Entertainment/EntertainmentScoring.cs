using CleaningSuite.Domain.Entertainment;

namespace CleaningSuite.Application.Entertainment;

/// <summary>
/// The AI grades two bounded dimensions; the server adds them to produce the
/// round score. Hints and answer reveals do not change anyone's score.
/// </summary>
public static class EntertainmentScoring
{
    public const int MaximumMeaningPoints = 7;
    public const int MaximumFinnishPoints = 3;
    public const int MaximumRoundPoints = MaximumMeaningPoints + MaximumFinnishPoints;

    public static int ScoreAnswer(EntertainmentAnswerEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.MeaningPoints is < 0 or > MaximumMeaningPoints ||
            evaluation.FinnishPoints is < 0 or > MaximumFinnishPoints ||
            evaluation.IsCorrect != (evaluation.MeaningPoints == MaximumMeaningPoints))
            throw new ArgumentException("AI grading result is outside the supported rubric.", nameof(evaluation));

        return evaluation.MeaningPoints + evaluation.FinnishPoints;
    }
}
