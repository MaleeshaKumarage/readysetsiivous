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
    public void Parse_MissingCandidates_Throws()
    {
        Assert.ThrowsAny<System.Exception>(() => GeminiSentenceService.Parse("{}"));
    }
}
