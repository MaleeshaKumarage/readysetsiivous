using CleaningSuite.Domain.Entertainment;
namespace CleaningSuite.Application.Entertainment;

public interface IEntertainmentAnswerEvaluator
{
    Task<EntertainmentAnswerEvaluation> EvaluateAsync(string sentenceEn, string referenceFi, string answer, CefrLevel level, CancellationToken ct = default);
}
public sealed class EntertainmentAiUnavailableException(
    string message = "AI is temporarily unavailable. Keep your answer and retry.",
    string? diagnostic = null) : Exception(message)
{
    /// <summary>Safe provider status detail for the player's opt-in error view. Never contains credentials.</summary>
    public string? Diagnostic { get; } = diagnostic;
}
