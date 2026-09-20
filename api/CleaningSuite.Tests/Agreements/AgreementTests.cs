using CleaningSuite.Domain.Agreements;

namespace CleaningSuite.Tests.Agreements;

public class AgreementTests
{
    [Fact]
    public void Agreement_Defaults_ToDraft()
    {
        var a = new Agreement { Slug = "readysetsiivous", Title = "Test" };
        Assert.Equal(Agreement.StatusDraft, a.Status);
        Assert.Empty(a.Signers);
        Assert.Null(a.SignedPdfPath);
        Assert.Null(a.CompletedUtc);
    }

    [Fact]
    public void AllSigned_True_WhenEverySignerSigned()
    {
        var a = new Agreement { Slug = "x", Title = "t" };
        a.Signers.Add(new Signer { Id = Guid.NewGuid(), Name = "A", Email = "a@x.fi", Status = Signer.StatusSigned });
        a.Signers.Add(new Signer { Id = Guid.NewGuid(), Name = "B", Email = "b@x.fi", Status = Signer.StatusPending });
        Assert.False(a.AllSigned);

        a.Signers[1].Status = Signer.StatusSigned;
        Assert.True(a.AllSigned);
    }
}
