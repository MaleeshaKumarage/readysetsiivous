using CleaningSuite.Api.Controllers;
using CleaningSuite.Application.Agreements.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Agreements;

public class PublicAgreementLinkTests
{
    [Fact]
    public async Task An_unknown_signing_link_tells_the_customer_to_ask_for_a_new_one()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetPublicAgreementQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((PublicAgreementDto?)null);
        var controller = new AgreementsPublicController(mediator.Object);
        var result = await controller.Get("not-a-real-token", default);
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("Ask the sender for a new signing link", notFound.Value!.ToString());
    }
}
