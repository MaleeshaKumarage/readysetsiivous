using CleaningSuite.Application.Common;
using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using FluentValidation.TestHelper;
using Moq;

namespace CleaningSuite.Tests.Leads;

public class LeadDomainTests
{
    [Fact]
    public void Create_RequiresName()
    {
        Assert.Throws<ArgumentException>(() => Lead.Create("  "));
    }

    [Fact]
    public void Create_NormalizesEmailsAndPeople()
    {
        var lead = Lead.Create(
            "  Acme  ",
            businessId: " B-1 ",
            mainContactName: " Ada ",
            mainContactEmail: " ada@acme.test ",
            mainContactPhone: " 555 ",
            emails: ["  team@acme.test ", "team@acme.test", " ", "sales@acme.test"],
            contactPersons:
            [
                new LeadContactPerson { Name = "  Bob  ", Email = " bob@acme.test ", Phone = " 1 ", Role = " Ops " },
                new LeadContactPerson { Name = "  ", Email = "skip@acme.test" },
            ],
            notes: "  hot  ");

        Assert.Equal("Acme", lead.Name);
        Assert.Equal("B-1", lead.BusinessId);
        Assert.Equal("Ada", lead.MainContactName);
        Assert.Equal("ada@acme.test", lead.MainContactEmail);
        Assert.Equal("555", lead.MainContactPhone);
        Assert.Equal(new[] { "team@acme.test", "sales@acme.test" }, lead.Emails);
        Assert.Single(lead.ContactPersons);
        Assert.Equal(("Bob", "bob@acme.test", "1", "Ops"), (
            lead.ContactPersons[0].Name,
            lead.ContactPersons[0].Email,
            lead.ContactPersons[0].Phone,
            lead.ContactPersons[0].Role));
        Assert.Equal("hot", lead.Notes);
        Assert.True(lead.IsActive);
    }

    [Fact]
    public void Update_NullCollections_PreserveExisting()
    {
        var lead = Lead.Create(
            "Acme",
            emails: ["ops@acme.test"],
            contactPersons: [new LeadContactPerson { Name = "Bob", Email = "bob@acme.test" }]);
        lead.Deactivate();

        lead.Update(
            name: "Acme Renamed",
            businessId: null,
            mainContactName: null,
            mainContactEmail: null,
            mainContactPhone: null,
            emails: null,
            contactPersons: null,
            notes: null,
            isActive: null);

        Assert.Equal("Acme Renamed", lead.Name);
        Assert.Equal(new[] { "ops@acme.test" }, lead.Emails);
        Assert.Single(lead.ContactPersons);
        Assert.Equal("Bob", lead.ContactPersons[0].Name);
        Assert.False(lead.IsActive);
    }

    [Fact]
    public void DeactivateAndReactivate_ToggleActivity()
    {
        var lead = Lead.Create("Acme");
        lead.Deactivate();
        Assert.False(lead.IsActive);
        lead.Reactivate();
        Assert.True(lead.IsActive);
    }
}

public class LeadHandlerTests
{
    [Fact]
    public async Task Create_PersistsMappedDto()
    {
        var repo = new Mock<ILeadRepository>();
        Lead? saved = null;
        repo.Setup(r => r.SaveAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()))
            .Callback<Lead, CancellationToken>((l, _) => saved = l)
            .Returns(Task.CompletedTask);

        var handler = new LeadHandlers.CreateLeadCommandHandler(repo.Object);
        var result = await handler.Handle(new CreateLeadCommand(
            "Acme",
            BusinessId: "B1",
            MainContactName: "Ada",
            MainContactEmail: "ada@acme.test",
            Emails: ["ops@acme.test"],
            ContactPersons: [new LeadContactPersonDto("Bob", "bob@acme.test", null, "Buyer")],
            Notes: "note"), default);

        Assert.NotNull(saved);
        Assert.Equal("Acme", result.Name);
        Assert.Equal(new[] { "ops@acme.test" }, result.Emails);
        Assert.Single(result.ContactPersons);
        Assert.Equal("Bob", result.ContactPersons[0].Name);
        repo.Verify(r => r.SaveAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_MissingLead_ThrowsNotFound()
    {
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Lead?)null);
        var handler = new LeadHandlers.UpdateLeadCommandHandler(repo.Object);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateLeadCommand(Guid.NewGuid(), "Acme"), default));
        repo.Verify(r => r.SaveAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_ActiveLead_IsRejected()
    {
        var lead = Lead.Create("Acme");
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.GetAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var handler = new LeadHandlers.DeleteLeadCommandHandler(repo.Object);
        await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            handler.Handle(new DeleteLeadCommand(lead.Id), default));
        repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_InactiveLead_Deletes()
    {
        var lead = Lead.Create("Acme");
        lead.Deactivate();
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.GetAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var handler = new LeadHandlers.DeleteLeadCommandHandler(repo.Object);
        await handler.Handle(new DeleteLeadCommand(lead.Id), default);
        repo.Verify(r => r.DeleteAsync(lead.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reactivate_RestoresActivity()
    {
        var lead = Lead.Create("Acme", mainContactName: "Ada");
        lead.Deactivate();
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.GetAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        await new LeadHandlers.ReactivateLeadCommandHandler(repo.Object).Handle(new(lead.Id), default);
        Assert.True(lead.IsActive);
        Assert.Equal("Ada", lead.MainContactName);
        repo.Verify(r => r.SaveAsync(lead, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_MapsPagedResults()
    {
        var lead = Lead.Create("Acme");
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.ListAsync(null, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Lead> { lead } as IReadOnlyList<Lead>, 1));
        var page = await new LeadHandlers.ListLeadsQueryHandler(repo.Object)
            .Handle(new ListLeadsQuery(null, 0, 10), default);
        Assert.Equal(1, page.Total);
        Assert.Equal("Acme", page.Items[0].Name);
    }
}

public class LeadValidatorTests
{
    [Fact]
    public void Create_EmptyName_IsInvalid()
    {
        var result = new LeadValidators.CreateLeadCommandValidator()
            .TestValidate(new CreateLeadCommand(""));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_InvalidEmail_IsInvalid()
    {
        var result = new LeadValidators.CreateLeadCommandValidator()
            .TestValidate(new CreateLeadCommand("Acme", MainContactEmail: "not-an-email"));
        result.ShouldHaveValidationErrorFor(x => x.MainContactEmail);
    }

    [Fact]
    public void Create_ValidPayload_Passes()
    {
        var result = new LeadValidators.CreateLeadCommandValidator()
            .TestValidate(new CreateLeadCommand(
                "Acme",
                MainContactEmail: "ada@acme.test",
                Emails: ["ops@acme.test"],
                ContactPersons: [new LeadContactPersonDto("Bob", "bob@acme.test", null, null)]));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
