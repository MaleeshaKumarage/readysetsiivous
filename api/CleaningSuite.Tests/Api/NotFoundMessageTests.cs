using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CleaningSuite.Application.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleaningSuite.Tests.Api;

public class NotFoundMessageTests
{
    [Fact]
    public async Task A_missing_record_names_what_was_not_found_and_does_not_echo_the_id()
    {
        var id = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var handled = await new DefaultExceptionHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultExceptionHandler>.Instance)
            .TryHandleAsync(http, new NotFoundException("Company", id), default);
        Assert.True(handled);
        Assert.Equal(404, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        var text = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.Contains("That company could not be found. Refresh the page and try again.", text);
        Assert.DoesNotContain(id.ToString(), text);
    }
}
