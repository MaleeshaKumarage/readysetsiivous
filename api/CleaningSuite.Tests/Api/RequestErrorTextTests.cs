using CleaningSuite.Api;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CleaningSuite.Tests.Api;

public class RequestErrorTextTests
{
    [Fact]
    public void Missing_partner_body_names_the_required_request_fields()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("", "A non-empty request body is required.");
        var problem = RequestErrorText.FromModelState(state, partnerRequest: true);
        Assert.Equal(new[] { "clientRequestId", "branchId", "fromDate", "toDate" }, problem.Errors.Keys);
        Assert.All(problem.Errors.Values, messages => Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message))));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public void Partner_field_conversion_keeps_the_specific_field_error()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("$.branchId", "The JSON value could not be converted to System.Guid.");
        var problem = RequestErrorText.FromModelState(state, partnerRequest: true);
        Assert.Single(problem.Errors);
        Assert.Equal(RequestErrorText.BadValue, Assert.Single(problem.Errors["branchId"]));
    }
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
    public void A_wrong_typed_body_value_under_the_root_key_keeps_its_own_message_and_is_not_masked()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("$", "The JSON value could not be converted to CleaningSuite.Application.Partners.CreatePartnerRequest.");
        var problem = RequestErrorText.FromModelState(state, partnerRequest: true);
        Assert.Equal(new[] { "$" }, problem.Errors.Keys.ToArray());
        Assert.Equal(new[] { RequestErrorText.BadValue }, problem.Errors["$"]);
    }

    [Fact]
    public void Generic_validation_keeps_the_framework_title_and_has_no_detail()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("name", "Name is required.");
        var problem = RequestErrorText.FromModelState(state);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.Null(problem.Detail);
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
