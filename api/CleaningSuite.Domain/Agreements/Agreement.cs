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
    public string OriginalPdfPath { get; set; } = "";
    public string Status { get; set; } = StatusDraft;
    public List<Signer> Signers { get; set; } = new();
    public string? SignedPdfPath { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public bool AllSigned => Signers.Count > 0 && Signers.All(s => s.Status == Signer.StatusSigned);
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
