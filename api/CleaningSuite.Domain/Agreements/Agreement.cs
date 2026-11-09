using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Agreements;

public class Agreement : BaseDocument
{
    public const string StatusDraft = "Draft";
    public const string StatusPartiallySigned = "PartiallySigned";
    public const string StatusCompleted = "Completed";
    public const string StatusCancelled = "Cancelled";

    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";
    public Guid CompanyId { get; set; }
    public bool IsActive { get; set; } = true;
    public string OriginalPdfPath { get; set; } = "";
    public string Status { get; set; } = StatusDraft;
    public List<Signer> Signers { get; set; } = new();
    public string? SignedPdfPath { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public bool AllSigned => Signers.Count > 0 && Signers.All(s => s.Status == Signer.StatusSigned);

    /// <summary>True once a signer has signed, by any of the signed markers, so the signature is evidence.</summary>
    public static bool HasSigned(Signer signer) => signer.Status == Signer.StatusSigned || signer.SignedAtUtc.HasValue
        || !string.IsNullOrWhiteSpace(signer.SignatureImagePath);

    public bool HasSignedEvidence => SignedPdfPath is not null
        || Status is StatusPartiallySigned or StatusCompleted
        || Signers.Any(HasSigned);
}

public class Signer
{
    public const string StatusPending = "Pending";
    public const string StatusSigned = "Signed";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Token { get; set; } = "";
    public string Status { get; set; } = StatusPending;
    public string? SignatureImagePath { get; set; }
    public string? TypedName { get; set; }
    public DateTime? SignedAtUtc { get; set; }
}
