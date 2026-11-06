using CleaningSuite.Api.Controllers;
using Xunit;

namespace CleaningSuite.Tests.Leads;

public class LeadImageLimitTests
{
    [Fact]
    public void The_request_body_limit_leaves_room_for_a_full_size_image_plus_form_overhead()
    {
        Assert.True(LeadsAdminController.LeadImageBodyLimitBytes >= LeadsAdminController.LeadImageMaxBytes + 1024 * 1024);
        Assert.Equal(5L * 1024 * 1024, LeadsAdminController.LeadImageMaxBytes);
    }
}
