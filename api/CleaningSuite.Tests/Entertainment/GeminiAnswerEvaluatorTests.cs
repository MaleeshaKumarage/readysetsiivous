using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Xunit;
namespace CleaningSuite.Tests.Entertainment;
[Collection(GeminiProviderCollection.Name)]
public class GeminiAnswerEvaluatorTests
{
    [Theory] [InlineData("Kissa nukkuu.")] [InlineData("Kissa on unessa.")]
    public async Task Correct_and_alternate_answers_preserved_without_invented_changes(string answer)
    {
        var provider = new GeminiFallbackTests.Provider((_, _) => Task.FromResult(GeminiFallbackTests.Response(new { isCorrect = true, meaningPoints = 7, finnishPoints = 3, correctedAnswer = answer, explanation = "This conveys the English meaning naturally.", corrections = Array.Empty<object>() })));
        var result = await new GeminiAnswerEvaluator(new(provider), GeminiFallbackTests.Config()).EvaluateAsync("The cat sleeps.", "Kissa nukkuu.", answer, CefrLevel.A1);
        Assert.True(result.IsCorrect); Assert.Equal(answer, result.CorrectedAnswer); Assert.Empty(result.Corrections); Assert.Equal(10, EntertainmentScoring.ScoreAnswer(result));
    }
    [Fact] public async Task Incorrect_answer_has_precise_correction_and_explanation()
    {
        var provider = new GeminiFallbackTests.Provider((_, _) => Task.FromResult(GeminiFallbackTests.Response(new { isCorrect = false, meaningPoints = 6, finnishPoints = 1, correctedAnswer = "Kissa nukkuu.", explanation = "Use the third-person singular verb.", corrections = new[] { new { original = "nukkua", replacement = "nukkuu", reason = "Third-person singular" } } })));
        var result = await new GeminiAnswerEvaluator(new(provider), GeminiFallbackTests.Config()).EvaluateAsync("The cat sleeps.", "Kissa nukkuu.", "Kissa nukkua.", CefrLevel.A1);
        Assert.False(result.IsCorrect); Assert.Single(result.Corrections); Assert.NotEmpty(result.Explanation); Assert.Equal(7, EntertainmentScoring.ScoreAnswer(result));
    }
    [Fact] public async Task Meaning_correct_answer_can_receive_finnish_correction_and_partial_quality_points()
    {
        var provider = new GeminiFallbackTests.Provider((_, _) => Task.FromResult(GeminiFallbackTests.Response(new { isCorrect = true, meaningPoints = 7, finnishPoints = 1, correctedAnswer = "Kissa nukkuu.", explanation = "The meaning is right; use the third-person verb form.", corrections = new[] { new { original = "nukkua", replacement = "nukkuu", reason = "Use the third-person singular verb." } } })));
        var result = await new GeminiAnswerEvaluator(new(provider), GeminiFallbackTests.Config()).EvaluateAsync("The cat sleeps.", "Kissa nukkuu.", "Kissa nukkua.", CefrLevel.A1);
        Assert.True(result.IsCorrect); Assert.Equal(8, EntertainmentScoring.ScoreAnswer(result)); Assert.Single(result.Corrections);
    }
    [Theory] [InlineData(false, 7, 3)] [InlineData(true, 6, 3)] [InlineData(false, -1, 2)] [InlineData(false, 0, 4)]
    public async Task Inconsistent_or_out_of_range_provider_grades_are_rejected(bool correct, int meaning, int finnish)
    {
        var provider = new GeminiFallbackTests.Provider((_, _) => Task.FromResult(GeminiFallbackTests.Response(new { isCorrect = correct, meaningPoints = meaning, finnishPoints = finnish, correctedAnswer = "Kissa nukkuu.", explanation = "Explanation.", corrections = Array.Empty<object>() })));
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiAnswerEvaluator(new(provider), GeminiFallbackTests.Config()).EvaluateAsync("The cat sleeps.", "Kissa nukkuu.", "Kissa nukkuu.", CefrLevel.A1));
    }
    [Fact] public async Task Invented_correction_rejected()
    {
        var provider = new GeminiFallbackTests.Provider((_, _) => Task.FromResult(GeminiFallbackTests.Response(new { isCorrect = false, meaningPoints = 0, finnishPoints = 0, correctedAnswer = "Kissa nukkuu.", explanation = "Wrong.", corrections = new[] { new { original = "absent", replacement = "nukkuu", reason = "Wrong" } } })));
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiAnswerEvaluator(new(provider), GeminiFallbackTests.Config()).EvaluateAsync("The cat sleeps.", "Kissa nukkuu.", "Kissa nukkua.", CefrLevel.A1));
    }
    [Theory] [InlineData(true, "Kissa on unessa.")] [InlineData(false, "Kissa nukkuu.")]
    public async Task Correct_answer_changes_and_wrong_answer_without_corrections_rejected(bool correct, string corrected)
    {
        var provider = new GeminiFallbackTests.Provider((_, _) => Task.FromResult(GeminiFallbackTests.Response(new { isCorrect = correct, meaningPoints = correct ? 7 : 6, finnishPoints = 3, correctedAnswer = corrected, explanation = "Explanation.", corrections = Array.Empty<object>() })));
        await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiAnswerEvaluator(new(provider), GeminiFallbackTests.Config()).EvaluateAsync("The cat sleeps.", "Kissa nukkuu.", "Kissa nukkuu.", CefrLevel.A1));
    }
    [Fact] public async Task Missing_key_evaluation_fails_without_call_or_secret()
    {
        var provider = new GeminiFallbackTests.Provider((_, _) => throw new Exception());
        var error = await Assert.ThrowsAsync<EntertainmentAiUnavailableException>(() => new GeminiAnswerEvaluator(new(provider), GeminiFallbackTests.Config(null)).EvaluateAsync("The cat sleeps.", "Kissa nukkuu.", "private answer", CefrLevel.A1));
        Assert.Equal(0, provider.Calls); Assert.DoesNotContain("private answer", error.Message);
    }
}
