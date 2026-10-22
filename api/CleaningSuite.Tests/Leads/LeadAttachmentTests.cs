using CleaningSuite.Application.Common;
using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using Moq;

namespace CleaningSuite.Tests.Leads;

public class LeadAttachmentTests
{
    [Fact]
    public void AddAttachment_RejectsAFourthFile()
    {
        var lead = Lead.Create("Acme", notes: "keep");
        lead.AddAttachment("a.png", "image/png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", 10);
        lead.AddAttachment("b.png", "image/png", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.png", 20);
        lead.AddAttachment("c.pdf", "application/pdf", "cccccccccccccccccccccccccccccccc.pdf", 30);

        Assert.Throws<InvalidOperationException>(() =>
            lead.AddAttachment("d.png", "image/png", "dddddddddddddddddddddddddddddddd.png", 40));
        Assert.Equal(3, lead.Attachments.Count);

        lead.Update("Renamed", null, null, null, null, null, null, null, null);
        Assert.Equal("Renamed", lead.Name);
        Assert.Equal("keep", lead.Notes);
        Assert.Equal(3, lead.Attachments.Count);
    }

    [Fact]
    public async Task AddHandler_FourthFile_IsRejected()
    {
        var lead = Lead.Create("Acme");
        lead.AddAttachment("a.png", "image/png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", 10);
        lead.AddAttachment("b.png", "image/png", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.png", 10);
        lead.AddAttachment("c.png", "image/png", "cccccccccccccccccccccccccccccccc.png", 10);
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.GetAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);

        var handler = new LeadHandlers.AddLeadAttachmentCommandHandler(repo.Object);
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new AddLeadAttachmentCommand(lead.Id, "d.png", "image/png", "dddddddddddddddddddddddddddddddd.png", 10), default));
        repo.Verify(r => r.SaveAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveHandler_MissingAttachment_IsNotFound()
    {
        var lead = Lead.Create("Acme");
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.GetAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var handler = new LeadHandlers.RemoveLeadAttachmentCommandHandler(repo.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RemoveLeadAttachmentCommand(lead.Id, Guid.NewGuid()), default));
    }
}
