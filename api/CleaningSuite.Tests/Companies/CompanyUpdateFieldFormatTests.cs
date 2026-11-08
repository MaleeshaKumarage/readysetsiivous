using CleaningSuite.Application.Companies;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Companies;

// An edit must meet the same field rules as creating a company.
public class CompanyUpdateFieldFormatTests
{
    private static UpdateCompanyCommand Update(string businessId = "1234567-8", string name = "Acme Oy", string? email = null)
        => new(Guid.NewGuid(), businessId, name, ContactEmail: email);

    [Theory]
    [InlineData("abc")]
    [InlineData("12345678")]
    [InlineData("1234567-")]
    [InlineData("123456-8")]
    public void Edited_business_ID_must_look_like_a_finnish_business_id(string businessId)
    {
        new CompanyValidators.UpdateCompanyCommandValidator().TestValidate(Update(businessId: businessId))
            .ShouldHaveValidationErrorFor(x => x.BusinessId);
    }

    [Fact]
    public void Edited_name_is_limited_to_200_characters()
    {
        new CompanyValidators.UpdateCompanyCommandValidator().TestValidate(Update(name: new string('x', 201)))
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("name@")]
    public void Edited_contact_email_must_be_valid(string email)
    {
        new CompanyValidators.UpdateCompanyCommandValidator().TestValidate(Update(email: email))
            .ShouldHaveValidationErrorFor(x => x.ContactEmail);
    }

    [Fact]
    public void A_valid_edit_passes()
    {
        new CompanyValidators.UpdateCompanyCommandValidator().TestValidate(Update(email: "name@company.fi")).ShouldNotHaveAnyValidationErrors();
    }
}
