using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CleaningSuite.Api;

/// <summary>
/// Turns model-binding failures into messages a person can act on. Framework messages can name internal .NET types,
/// for example when a JSON value cannot be converted to a request type.
/// </summary>
public static class RequestErrorText
{
    public const string BadValue = "This value is not in the expected format. Check it and try again.";
    public const string BodyRequired = "The body field is required.";
    // ASP.NET's message when a request has no body at all.
    private const string EmptyBodyMessage = "A non-empty request body is required.";

    // A model error can carry only an exception, so its message may be null; never echo exception text.
    public static string Plain(string? message) =>
        string.IsNullOrWhiteSpace(message) || message.Contains("could not be converted to", StringComparison.OrdinalIgnoreCase) ? BadValue : message;

    /// <summary>Builds the 400 response from model state: field names without the JSON path marker, plain messages, and no body-required noise when a field error explains the problem.</summary>
    public static ValidationProblemDetails FromModelState(ModelStateDictionary state, bool partnerRequest = false)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var entry in state)
        {
            var field = entry.Key.StartsWith("$.", StringComparison.Ordinal) ? entry.Key[2..] : entry.Key;
            var messages = entry.Value.Errors.Select(e => Plain(e.ErrorMessage)).Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
            if (messages.Length > 0) errors[field] = messages;
        }
        if (errors.Count > 1 && errors.TryGetValue("body", out var body) && body.All(m => m == BodyRequired)) errors.Remove("body");
        if (partnerRequest && IsMissingPartnerBody(errors))
        {
            errors = new Dictionary<string, string[]>
            {
                ["clientRequestId"] = ["A request identifier is required."],
                ["branchId"] = ["Choose the property for this cleaning."],
                ["fromDate"] = ["Choose the first day of the cleaning."],
                ["toDate"] = ["Choose the last day of the cleaning."]
            };
            return new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest, Title = "Validation failed", Detail = "Check the request fields and try again." };
        }
        // Every other 400 keeps the framework contract: the default title and no detail.
        return new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest };
    }

    // Only a request with no body, or with the body-required message alone, is replaced by the field prompts. A wrong-typed
    // value also arrives under "$" but carries its own conversion message, so its specific error is kept.
    private static bool IsMissingPartnerBody(Dictionary<string, string[]> errors) =>
        errors.Count > 0
        && errors.Keys.All(k => k is "" or "$" or "body")
        && errors.Values.SelectMany(messages => messages).All(m => m is BodyRequired or EmptyBodyMessage);
}
