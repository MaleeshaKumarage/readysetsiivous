using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Agreements.Queries;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Agreements;

public class AgreementSignerManagementTests
{
    private const string Tenant = "synthetic";

    private static Signer Pending(string name, string email) =>
        new() { Name = name, Email = email, Token = "token-" + email, Status = Signer.StatusPending };

    private static Signer Signed(string name, string email) => new()
    {
        Name = name, Email = email, Token = "token-" + email, Status = Signer.StatusSigned,
        SignedAtUtc = DateTime.UtcNow, SignatureImagePath = "signatures/" + email + ".png",
    };

    private static Agreement Build(string status, params Signer[] signers)
    {
        var agreement = new Agreement { Id = Guid.NewGuid(), Status = status, OriginalPdfPath = "original.pdf", Title = "Contract" };
        agreement.Signers.AddRange(signers);
        return agreement;
    }

    private static (Mock<IAgreementRepository> repo, ITenantContext context) Repo(Agreement agreement)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns(Tenant);
        var repo = new Mock<IAgreementRepository>();
        repo.Setup(x => x.GetAsync(Tenant, agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        repo.Setup(x => x.SaveAsync(Tenant, It.IsAny<Agreement>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (repo, context.Object);
    }

    private static async Task<AgreementConflictException> RemoveRefused(Agreement agreement, Guid signerId)
    {
        var (repo, context) = Repo(agreement);
        var handler = new RemoveSignerHandler(context, repo.Object);
        return await Assert.ThrowsAsync<AgreementConflictException>(() =>
            handler.Handle(new RemoveSignerCommand(agreement.Id, signerId), default));
    }

    private static async Task<AgreementConflictException> AddRefused(Agreement agreement, string email)
    {
        var (repo, context) = Repo(agreement);
        var handler = new AddSignerHandler(context, repo.Object);
        return await Assert.ThrowsAsync<AgreementConflictException>(() =>
            handler.Handle(new AddSignerCommand(agreement.Id, "New Signer", email), default));
    }

    [Fact]
    public async Task Adding_a_signer_to_a_draft_saves_a_pending_signer_with_a_token()
    {
        var agreement = Build(Agreement.StatusDraft, Pending("Anna", "anna@example.test"));
        var (repo, context) = Repo(agreement);
        var handler = new AddSignerHandler(context, repo.Object);

        await handler.Handle(new AddSignerCommand(agreement.Id, "Ben", "ben@example.test"), default);

        var saved = agreement.Signers.Single(s => s.Email == "ben@example.test");
        Assert.Equal(Signer.StatusPending, saved.Status);
        Assert.False(string.IsNullOrWhiteSpace(saved.Token));
        repo.Verify(x => x.SaveAsync(Tenant, agreement, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Adding_a_signer_to_a_partially_signed_agreement_is_allowed()
    {
        var agreement = Build(Agreement.StatusPartiallySigned, Signed("Anna", "anna@example.test"), Pending("Ben", "ben@example.test"));
        var (repo, context) = Repo(agreement);
        await new AddSignerHandler(context, repo.Object).Handle(new AddSignerCommand(agreement.Id, "Cara", "cara@example.test"), default);
        Assert.Equal(3, agreement.Signers.Count);
    }

    [Theory]
    [InlineData(Agreement.StatusCompleted)]
    [InlineData(Agreement.StatusCancelled)]
    public async Task Adding_a_signer_to_a_closed_agreement_is_a_conflict_message_not_a_server_error(string status)
    {
        var ex = await AddRefused(Build(status, Signed("Anna", "anna@example.test")), "ben@example.test");
        Assert.Contains("closed", ex.Message);
        Assert.Contains("Create a new agreement", ex.Message);
    }

    [Fact]
    public async Task Adding_an_email_that_is_already_a_signer_is_refused_whatever_the_case()
    {
        var agreement = Build(Agreement.StatusDraft, Pending("Anna", "anna@example.test"));
        var ex = await AddRefused(agreement, "ANNA@example.test");
        Assert.Contains("already a signer", ex.Message);
        Assert.Single(agreement.Signers);
    }

    [Fact]
    public async Task Removing_a_pending_signer_from_a_draft_removes_only_that_signer()
    {
        var anna = Pending("Anna", "anna@example.test");
        var ben = Pending("Ben", "ben@example.test");
        var agreement = Build(Agreement.StatusDraft, anna, ben);
        var (repo, context) = Repo(agreement);

        await new RemoveSignerHandler(context, repo.Object).Handle(new RemoveSignerCommand(agreement.Id, ben.Id), default);

        Assert.Equal(new[] { anna.Id }, agreement.Signers.Select(s => s.Id).ToArray());
        Assert.Equal(Agreement.StatusDraft, agreement.Status);
    }

    [Fact]
    public async Task Removing_a_pending_signer_while_others_are_still_pending_keeps_the_partial_status()
    {
        var anna = Signed("Anna", "anna@example.test");
        var ben = Pending("Ben", "ben@example.test");
        var cara = Pending("Cara", "cara@example.test");
        var agreement = Build(Agreement.StatusPartiallySigned, anna, ben, cara);
        var (repo, context) = Repo(agreement);

        await new RemoveSignerHandler(context, repo.Object).Handle(new RemoveSignerCommand(agreement.Id, ben.Id), default);

        Assert.Equal(2, agreement.Signers.Count);
        Assert.Equal(Agreement.StatusPartiallySigned, agreement.Status);
    }

    [Fact]
    public async Task Removing_a_signer_who_has_signed_is_refused_and_keeps_the_evidence()
    {
        var anna = Signed("Anna", "anna@example.test");
        var agreement = Build(Agreement.StatusPartiallySigned, anna, Pending("Ben", "ben@example.test"));
        var ex = await RemoveRefused(agreement, anna.Id);
        Assert.Contains("already signed", ex.Message);
        Assert.Equal(2, agreement.Signers.Count);
    }

    [Fact]
    public async Task A_signer_with_a_signature_image_but_no_status_is_still_treated_as_signed()
    {
        var anna = Pending("Anna", "anna@example.test");
        anna.SignatureImagePath = "signatures/anna.png";
        var agreement = Build(Agreement.StatusPartiallySigned, anna, Pending("Ben", "ben@example.test"));
        var ex = await RemoveRefused(agreement, anna.Id);
        Assert.Contains("already signed", ex.Message);
    }

    [Fact]
    public async Task Removing_the_last_signer_is_refused_because_an_agreement_needs_one()
    {
        var anna = Pending("Anna", "anna@example.test");
        var agreement = Build(Agreement.StatusDraft, anna);
        var ex = await RemoveRefused(agreement, anna.Id);
        Assert.Contains("at least one signer", ex.Message);
        Assert.Single(agreement.Signers);
    }

    [Fact]
    public async Task Removing_the_only_pending_signer_when_everyone_else_signed_is_refused_so_the_agreement_can_still_finish()
    {
        var anna = Signed("Anna", "anna@example.test");
        var ben = Pending("Ben", "ben@example.test");
        var agreement = Build(Agreement.StatusPartiallySigned, anna, ben);
        var ex = await RemoveRefused(agreement, ben.Id);
        Assert.Contains("unable to finish", ex.Message);
        Assert.Equal(2, agreement.Signers.Count);
    }

    [Theory]
    [InlineData(Agreement.StatusCompleted)]
    [InlineData(Agreement.StatusCancelled)]
    public async Task Removing_a_signer_from_a_closed_agreement_is_a_conflict_message(string status)
    {
        var agreement = Build(status, Signed("Anna", "anna@example.test"), Pending("Ben", "ben@example.test"));
        var ex = await RemoveRefused(agreement, agreement.Signers[1].Id);
        Assert.Contains("closed", ex.Message);
        Assert.Equal(2, agreement.Signers.Count);
    }

    [Fact]
    public async Task Removing_a_signer_that_is_already_gone_succeeds_quietly()
    {
        var agreement = Build(Agreement.StatusDraft, Pending("Anna", "anna@example.test"), Pending("Ben", "ben@example.test"));
        var (repo, context) = Repo(agreement);
        await new RemoveSignerHandler(context, repo.Object).Handle(new RemoveSignerCommand(agreement.Id, Guid.NewGuid()), default);
        Assert.Equal(2, agreement.Signers.Count);
    }

    [Fact]
    public async Task Cancelling_revokes_public_file_access_for_every_signer_including_those_who_signed()
    {
        var anna = Signed("Anna", "anna@example.test");
        var ben = Pending("Ben", "ben@example.test");
        var agreement = Build(Agreement.StatusPartiallySigned, anna, ben);
        agreement.IsActive = true;
        var (repo, context) = Repo(agreement);
        repo.Setup(x => x.FindBySignerTokenAsync(Tenant, anna.Token, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        repo.Setup(x => x.FindBySignerTokenAsync(Tenant, ben.Token, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        await new CancelAgreementHandler(context, repo.Object).Handle(new CancelAgreementCommand(agreement.Id), default);

        var fileHandler = new GetAgreementFileHandler(context, repo.Object);
        Assert.Null(await fileHandler.Handle(new GetAgreementFileQuery(anna.Token), default));
        Assert.Null(await fileHandler.Handle(new GetAgreementFileQuery(ben.Token), default));
    }

    [Fact]
    public async Task Cancelling_keeps_the_signed_evidence_and_the_admin_document_path()
    {
        var anna = Signed("Anna", "anna@example.test");
        var agreement = Build(Agreement.StatusPartiallySigned, anna, Pending("Ben", "ben@example.test"));
        agreement.SignedPdfPath = "signed/evidence.pdf";
        var (repo, context) = Repo(agreement);

        await new CancelAgreementHandler(context, repo.Object).Handle(new CancelAgreementCommand(agreement.Id), default);

        Assert.Equal(Agreement.StatusCancelled, agreement.Status);
        Assert.Equal("signed/evidence.pdf", agreement.SignedPdfPath);
        Assert.Equal("signatures/anna@example.test.png", agreement.Signers[0].SignatureImagePath);
        var path = await new GetAgreementDocumentPathHandler(context, repo.Object).Handle(new GetAgreementDocumentPathQuery(agreement.Id), default);
        Assert.Equal("original.pdf", path);
    }

    [Fact]
    public async Task An_active_pending_signer_can_still_view_the_file_before_cancellation()
    {
        var ben = Pending("Ben", "ben@example.test");
        var agreement = Build(Agreement.StatusDraft, ben);
        var (repo, context) = Repo(agreement);
        repo.Setup(x => x.FindBySignerTokenAsync(Tenant, ben.Token, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        Assert.Equal("original.pdf", await new GetAgreementFileHandler(context, repo.Object).Handle(new GetAgreementFileQuery(ben.Token), default));
    }
}
