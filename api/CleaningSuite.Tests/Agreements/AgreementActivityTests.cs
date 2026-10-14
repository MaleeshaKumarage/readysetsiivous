using System.Text.Json;
using System.Text.Json.Nodes;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public class AgreementActivityTests
{
    private readonly Mock<IAgreementRepository> _repository = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Agreement _agreement = new()
    {
        IsActive = false, Slug = "existing-agreement", Title = "Existing agreement", Code = "AG-42",
        CompanyId = Guid.NewGuid(), OriginalPdfPath = "original.pdf", SignedPdfPath = "signed.pdf",
        CompletedUtc = DateTime.UtcNow.AddDays(-5),
        Signers = [new() { Name = "First signer", Email = "first@example.invalid", Token = "preserved-first-token",
            Status = Signer.StatusSigned, SignatureImagePath = "signature.png", TypedName = "First signer", SignedAtUtc = DateTime.UtcNow.AddDays(-6) },
            new() { Name = "Second signer", Email = "second@example.invalid", Token = "preserved-second-token" }]
    };

    public AgreementActivityTests()
    {
        _tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        _repository.Setup(x => x.GetAsync("tenant-a", _agreement.Id, default)).ReturnsAsync(_agreement);
    }
    private SetAgreementActiveHandler Handler() => new(_tenant.Object, _repository.Object);

    [Theory]
    [InlineData(Agreement.StatusDraft)]
    [InlineData(Agreement.StatusPartiallySigned)]
    [InlineData(Agreement.StatusCompleted)]
    public async Task ReactivationPreservesSigningEvidenceAndAllOtherMetadata(string status)
    {
        _agreement.Status = status;
        var before = JsonSerializer.SerializeToNode(_agreement)!;
        await Handler().Handle(new(_agreement.Id, true), default);
        Assert.True(_agreement.IsActive);
        var after = JsonSerializer.SerializeToNode(_agreement)!;
        foreach (var key in new[] { "IsActive", "UpdatedUtc" }) { before.AsObject().Remove(key); after.AsObject().Remove(key); }
        Assert.True(JsonNode.DeepEquals(before, after));
        _repository.Verify(x => x.SaveAsync("tenant-a", _agreement, default), Times.Once);
    }

    [Fact]
    public async Task CancelledAgreementCannotBeReactivatedAndIsNotMutatedOrSaved()
    {
        _agreement.Status = Agreement.StatusCancelled;
        var before = JsonSerializer.Serialize(_agreement);
        var error = await Assert.ThrowsAsync<AgreementConflictException>(() => Handler().Handle(new(_agreement.Id, true), default));
        Assert.Contains("Cancelled agreements cannot be reactivated", error.Message);
        Assert.Equal(before, JsonSerializer.Serialize(_agreement));
        _repository.Verify(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Agreement>(), default), Times.Never);
    }

    [Fact]
    public async Task MissingAgreementReturnsNotFoundWithoutSaving()
    {
        _repository.Setup(x => x.GetAsync("tenant-a", _agreement.Id, default)).ReturnsAsync((Agreement?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => Handler().Handle(new(_agreement.Id, true), default));
        _repository.Verify(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Agreement>(), default), Times.Never);
    }
}
