namespace CleaningSuite.Application.Entertainment;

/// <summary>
/// Pure reveal-count scoring. Returns score deltas (playerId -> delta) for each
/// game event; callers accumulate deltas onto running scores.
/// </summary>
public static class EntertainmentScoring
{
    public const int TranslatorHintReveal = 1; // each other player
    public const int CleanSolveBonus = 5;      // translator, zero reveals
    public const int SkipPenalty = 1;          // each other player
    public const int FullAnswerReveal = 2;     // each other player (revealer excluded)

    /// <summary>Translator unwrapped a hint word: every other player scores.</summary>
    public static IReadOnlyDictionary<Guid, int> TranslatorRevealedHint(
        IReadOnlyList<Guid> allPlayers,
        Guid translatorId)
        => AwardEach(allPlayers, exclude: translatorId, TranslatorHintReveal);

    /// <summary>Translator tapped Done with zero hints revealed: translator scores.</summary>
    public static IReadOnlyDictionary<Guid, int> CleanSolve(Guid translatorId)
        => new Dictionary<Guid, int> { [translatorId] = CleanSolveBonus };

    /// <summary>Translator skipped: every other player scores.</summary>
    public static IReadOnlyDictionary<Guid, int> Skipped(
        IReadOnlyList<Guid> allPlayers,
        Guid translatorId)
        => AwardEach(allPlayers, exclude: translatorId, SkipPenalty);

    /// <summary>An observer revealed the full Finnish answer: everyone else scores.</summary>
    public static IReadOnlyDictionary<Guid, int> AnswerRevealed(
        IReadOnlyList<Guid> allPlayers,
        Guid revealerId)
        => AwardEach(allPlayers, exclude: revealerId, FullAnswerReveal);

    private static IReadOnlyDictionary<Guid, int> AwardEach(
        IReadOnlyList<Guid> allPlayers,
        Guid exclude,
        int points)
    {
        var deltas = new Dictionary<Guid, int>();
        foreach (var playerId in allPlayers)
        {
            if (playerId != exclude)
                deltas[playerId] = points;
        }
        return deltas;
    }
}
