using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class GeminiSentenceServiceTests
{
    [Fact]
    public void BuildPrompt_IncludesDescriptionAndLevel()
    {
        var prompt = GeminiSentenceService.BuildPrompt("travel and airports", CefrLevel.B2);

        Assert.Contains("B2", prompt);
        Assert.Contains("travel and airports", prompt);
        Assert.Contains("sentenceEn", prompt);
        Assert.Contains("sentenceFi", prompt);
        Assert.Contains("baseForms", prompt);
    }

    [Fact]
    public void BuildPrompt_ListsPreviousSentencesToAvoid()
    {
        var prompt = GeminiSentenceService.BuildPrompt(
            "travel", CefrLevel.B1, new[] { "We went to Sweden and Norway.", "The train was late." });

        Assert.Contains("Previous sentences", prompt);
        Assert.Contains("We went to Sweden and Norway.", prompt);
        Assert.Contains("The train was late.", prompt);
        Assert.Contains("Vary the sentence", prompt);
    }

    [Fact]
    public void Parse_ValidResponse_ExtractsSentence()
    {
        var json = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "{\"sentenceEn\":\"The cat sits.\",\"sentenceFi\":\"Kissa istuu.\",\"baseForms\":[\"cat\",\"sit\"]}" }
                    ]
                  }
                }
              ]
            }
            """;

        var result = GeminiSentenceService.Parse(json);

        Assert.Equal("The cat sits.", result.SentenceEn);
        Assert.Equal("Kissa istuu.", result.SentenceFi);
        Assert.Equal(new[] { "cat", "sit" }, result.BaseForms);
    }

    [Fact]
    public void Parse_ObjectBaseForms_ExtractsFinnish()
    {
        var json = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "{\"sentenceEn\":\"We missed our train.\",\"sentenceFi\":\"Myöhästyimme junasta.\",\"baseForms\":[{\"en\":\"miss\",\"fi\":\"myöhästyä\"},{\"en\":\"train\",\"fi\":\"juna\"}]}" }
                    ]
                  }
                }
              ]
            }
            """;

        var result = GeminiSentenceService.Parse(json);

        Assert.Equal("We missed our train.", result.SentenceEn);
        Assert.Equal("Myöhästyimme junasta.", result.SentenceFi);
        Assert.Equal(new[] { "myöhästyä", "juna" }, result.BaseForms);
    }

    [Fact]
    public void Parse_MissingCandidates_Throws()
    {
        Assert.ThrowsAny<System.Exception>(() => GeminiSentenceService.Parse("{}"));
    }
}
