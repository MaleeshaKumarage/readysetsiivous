using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleaningSuite.Tests.Api;

public class ValidationMessageTests
{
    private static async Task<string> Respond(ValidationException failure)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        Assert.True(await new ValidationExceptionHandler().TryHandleAsync(http, failure, default));
        http.Response.Body.Position = 0;
        return await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
    }

    [Fact]
    public async Task A_message_thrown_without_field_errors_reaches_the_client_as_the_detail()
    {
        var text = await Respond(new ValidationException("Choose a supported inclusive date range of at most 367 days."));
        Assert.Contains("Choose a supported inclusive date range of at most 367 days.", text);
    }

    [Fact]
    public async Task Field_errors_are_still_returned_by_field()
    {
        var text = await Respond(new ValidationException([new ValidationFailure("Name", "Name is mandatory.")]));
        Assert.Contains("\"Name\"", text);
        Assert.Contains("Name is mandatory.", text);
    }
}
