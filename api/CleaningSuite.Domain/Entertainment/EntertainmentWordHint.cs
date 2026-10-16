namespace CleaningSuite.Domain.Entertainment;
public record EntertainmentWordHint(string English, string Finnish);
public record EntertainmentCorrection(string Original, string Replacement, string Reason);
public record EntertainmentAnswerEvaluation(
    bool IsCorrect,
    string CorrectedAnswer,
    string Explanation,
    IReadOnlyList<EntertainmentCorrection> Corrections,
    int MeaningPoints = 0,
    int FinnishPoints = 0,
    int ScorePoints = 0,
    IReadOnlyList<string>? GrammarRules = null);
