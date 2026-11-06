using CleaningSuite.Application.Companies;
using Xunit;

namespace CleaningSuite.Tests.Companies;

public class CompanyConflictMessageTests
{
    [Fact]
    public void A_duplicate_business_id_says_what_to_do_next()
    {
        var message = new CompanyConflictException().Message;
        Assert.Contains("already exists in this tenant", message);
        Assert.Contains("open the existing company", message);
    }
}
