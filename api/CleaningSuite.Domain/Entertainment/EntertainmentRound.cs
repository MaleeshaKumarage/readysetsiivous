using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Entertainment;

/// <summary>
/// Event log for one round of the game: the sentence shown, who translated, and
/// (later) the reveal events. Source of truth for game history.
/// </summary>
public class EntertainmentRound : BaseDocument
{
    public Guid GroupId { get; set; }
    public int RoundNumber { get; set; }
    public Guid TranslatorPlayerId { get; set; }
    public string SentenceEn { get; set; } = string.Empty;
    public string SentenceFi { get; set; } = string.Empty;
    public List<string> BaseForms { get; set; } = new();
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>Snapshot of every player's score when the round ended (null while live).</summary>
    public Dictionary<Guid, int>? FinalScores { get; set; }

    public static EntertainmentRound Create(
        Guid groupId,
        int roundNumber,
        Guid translatorPlayerId,
        string sentenceEn,
        string sentenceFi,
        IEnumerable<string> baseForms)
    {
        if (string.IsNullOrWhiteSpace(sentenceEn))
            throw new ArgumentException("English sentence is mandatory.", nameof(sentenceEn));

        return new EntertainmentRound
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            GroupId = groupId,
            RoundNumber = roundNumber,
            TranslatorPlayerId = translatorPlayerId,
            SentenceEn = sentenceEn,
            SentenceFi = sentenceFi,
            BaseForms = baseForms.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).ToList(),
            StartedUtc = DateTime.UtcNow,
        };
    }
}
