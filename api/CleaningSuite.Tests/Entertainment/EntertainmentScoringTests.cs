using CleaningSuite.Application.Entertainment;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class EntertainmentScoringTests
{
    private static readonly Guid Translator = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly List<Guid> Players = new() { Translator, A, B };

    [Fact]
    public void TranslatorRevealedHint_AwardsEachOtherPlayer()
    {
        var deltas = EntertainmentScoring.TranslatorRevealedHint(Players, Translator);

        Assert.Equal(1, deltas[A]);
        Assert.Equal(1, deltas[B]);
        Assert.False(deltas.ContainsKey(Translator));
    }

    [Fact]
    public void CleanSolve_AwardsTranslator()
    {
        var deltas = EntertainmentScoring.CleanSolve(Translator);

        Assert.Equal(5, deltas[Translator]);
        Assert.Single(deltas);
    }

    [Fact]
    public void Skipped_AwardsEachOtherPlayer()
    {
        var deltas = EntertainmentScoring.Skipped(Players, Translator);

        Assert.Equal(1, deltas[A]);
        Assert.Equal(1, deltas[B]);
        Assert.False(deltas.ContainsKey(Translator));
    }

    [Fact]
    public void AnswerRevealed_AwardsEveryoneExceptRevealer()
    {
        var deltas = EntertainmentScoring.AnswerRevealed(Players, A);

        Assert.Equal(2, deltas[Translator]);
        Assert.Equal(2, deltas[B]);
        Assert.False(deltas.ContainsKey(A));
    }
}
