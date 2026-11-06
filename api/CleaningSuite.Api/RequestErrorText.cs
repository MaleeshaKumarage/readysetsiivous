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

    // A model error can carry only an exception, so its message may be null; never echo exception text.
    public static string Plain(string? message) =>
        string.IsNullOrWhiteSpace(message) || message.Contains("could not be converted to", StringComparison.OrdinalIgnoreCase) ? BadValue : message;

    /// <summary>Builds the 400 response from model state: field names without the JSON path marker, plain messages, and no body-required noise when a field error explains the problem.</summary>
    public static ValidationProblemDetails FromModelState(ModelStateDictionary state)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var entry in state)
        {
            var field = entry.Key.StartsWith("$.", StringComparison.Ordinal) ? entry.Key[2..] : entry.Key;
            var messages = entry.Value.Errors.Select(e => Plain(e.ErrorMessage)).Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
            if (messages.Length > 0) errors[field] = messages;
        }
        if (errors.Count > 1 && errors.TryGetValue("body", out var body) && body.All(m => m == BodyRequired)) errors.Remove("body");
        return new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest };
    }
}
