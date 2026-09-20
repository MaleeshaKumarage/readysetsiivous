using CleaningSuite.Application.Agreements;

namespace CleaningSuite.Tests.Agreements;

public class AgreementTokensTests
{
    [Fact]
    public void New_Produces_Unique_UrlSafe_Tokens()
    {
        var a = AgreementTokens.New();
        var b = AgreementTokens.New();
        Assert.NotEqual(a, b);
        Assert.All(a, c => Assert.False(c is '+' or '/' or '='));
    }
}
