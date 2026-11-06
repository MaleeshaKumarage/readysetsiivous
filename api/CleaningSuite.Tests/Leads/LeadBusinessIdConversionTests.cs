using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using Xunit;

namespace CleaningSuite.Tests.Leads;

public class LeadBusinessIdConversionTests
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567-89")]
    [InlineData("ABC-DEFG")]
    public void Conversion_refuses_a_business_id_that_is_not_in_the_company_format(string businessId)
    {
        var error = Assert.Throws<ValidationException>(() => LeadValidation.Conversion(new LeadFields { BusinessId = businessId }));
        Assert.Contains(error.Errors, e => e.PropertyName == "BusinessId" && e.ErrorMessage.Contains("1234567-8"));
    }

    [Fact]
    public void Conversion_accepts_a_business_id_in_the_company_format()
    {
        LeadValidation.Conversion(new LeadFields { BusinessId = "1234567-8" });
    }
}
