using CleaningSuite.Infrastructure.Leads;
using Xunit;

namespace CleaningSuite.Tests.Leads;

public class LeadAudioMessageTests
{
    [Fact]
    public void An_unreadable_recording_says_to_record_again_or_use_a_supported_format()
    {
        Assert.Contains("Record again", LeadAudioProbe.UnreadableMessage);
        Assert.Contains("WebM, Ogg or M4A", LeadAudioProbe.UnreadableMessage);
    }
}
