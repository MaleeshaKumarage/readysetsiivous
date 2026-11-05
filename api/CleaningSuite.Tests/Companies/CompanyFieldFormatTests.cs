using CleaningSuite.Application.Companies;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Companies;

public class CompanyFieldFormatTests
{
    private static CreateCompanyCommand Create(string businessId = "1234567-8", string name = "Acme Oy", string? email = null)
        => new(businessId, name, ContactEmail: email);

    [Theory]
    [InlineData("abc")]
    [InlineData("12345678")]
    [InlineData("1234567-")]
    [InlineData("123456-8")]
    public void Business_ID_must_look_like_a_finnish_business_id(string businessId)
    {
        var result = new CompanyValidators.CreateCompanyCommandValidator().TestValidate(Create(businessId: businessId));
        result.ShouldHaveValidationErrorFor(x => x.BusinessId)
            .WithErrorMessage("Business ID must look like 1234567-8: seven digits, a dash and one check digit. Check the company's registration, then try again.");
    }

    [Fact]
    public void Valid_business_ID_passes()
    {
        new CompanyValidators.CreateCompanyCommandValidator().TestValidate(Create()).ShouldNotHaveValidationErrorFor(x => x.BusinessId);
    }

    [Fact]
    public void Company_name_over_200_characters_is_refused_with_a_fix()
    {
        var result = new CompanyValidators.CreateCompanyCommandValidator().TestValidate(Create(name: new string('x', 201)));
        result.ShouldHaveValidationErrorFor(x => x.Name).WithErrorMessage("Company name can be at most 200 characters. Shorten it, then save again.");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("name@")]
    public void Malformed_contact_email_is_refused(string email)
    {
        var result = new CompanyValidators.CreateCompanyCommandValidator().TestValidate(Create(email: email));
        result.ShouldHaveValidationErrorFor(x => x.ContactEmail);
    }

    [Fact]
    public void Empty_contact_email_is_allowed()
    {
        new CompanyValidators.CreateCompanyCommandValidator().TestValidate(Create(email: null)).ShouldNotHaveValidationErrorFor(x => x.ContactEmail);
    }
}
