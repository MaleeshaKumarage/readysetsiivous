using CleaningSuite.Api;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CleaningSuite.Tests.Api;

public class RequestErrorTextTests
{
    [Fact]
    public void A_type_conversion_message_names_no_internal_type()
    {
        var message = "The JSON value could not be converted to CleaningSuite.Application.Partners.CreatePartnerRequest. Path: $.branchId | LineNumber: 0.";
        Assert.Equal(RequestErrorText.BadValue, RequestErrorText.Plain(message));
    }

    [Fact]
    public void Other_messages_pass_through_unchanged()
    {
        Assert.Equal("Enter a valid email address.", RequestErrorText.Plain("Enter a valid email address."));
    }

    [Fact]
    public void A_bad_field_is_reported_by_name_and_the_body_noise_is_dropped()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("body", RequestErrorText.BodyRequired);
        state.AddModelError("$.branchId", "The JSON value could not be converted to CleaningSuite.Application.Partners.CreatePartnerRequest. Path: $.branchId.");
        var problem = RequestErrorText.FromModelState(state);
        Assert.Equal(new[] { "branchId" }, problem.Errors.Keys.ToArray());
        Assert.Equal(new[] { RequestErrorText.BadValue }, problem.Errors["branchId"]);
    }

    [Fact]
    public void A_body_that_is_missing_alone_still_says_so()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("body", RequestErrorText.BodyRequired);
        var problem = RequestErrorText.FromModelState(state);
        Assert.Equal(new[] { RequestErrorText.BodyRequired }, problem.Errors["body"]);
    }
}
