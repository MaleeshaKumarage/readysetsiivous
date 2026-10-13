using CleaningSuite.Domain.Entertainment;

namespace CleaningSuite.Application.Entertainment;

public record GeminiSentence(
    string SentenceEn,
    string SentenceFi,
    IReadOnlyList<string> BaseForms);

/// <summary>Generates a level-appropriate English sentence (and its Finnish
/// translation + base forms) from a group description via the Gemini API.</summary>
public interface IGeminiSentenceService
{
    Task<GeminiSentence> GenerateAsync(
        string description,
        CefrLevel level,
        string apiKey,
        IReadOnlyList<string> previousSentences,
        CancellationToken ct = default);
}
