using CleaningSuite.Domain.Entertainment;

namespace CleaningSuite.Application.Entertainment;

public record GeminiSentence(
    string SentenceEn,
    string SentenceFi,
    IReadOnlyList<string> BaseForms)
{
    public bool IsSample { get; init; }
    public IReadOnlyList<EntertainmentWordHint> Hints { get; init; } = Array.Empty<EntertainmentWordHint>();
}

/// <summary>Generates a level-appropriate English sentence (and its Finnish
/// translation + base forms) via Gemini using the server credential.</summary>
public interface IGeminiSentenceService
{
    Task<GeminiSentence> GenerateAsync(
        string description,
        CefrLevel level,
        string apiKey,
        IReadOnlyList<string> previousSentences,
        CancellationToken ct = default);
}
