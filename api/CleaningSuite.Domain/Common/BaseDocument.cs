using Marten.Metadata;

namespace CleaningSuite.Domain.Common;

/// <summary>
/// Common fields on every Marten document. IVersioned makes Marten hydrate the
/// concurrency version on load and check it on update.
/// </summary>
public abstract class BaseDocument : IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Optimistic concurrency version (GUID in Marten 8), maintained by Marten.</summary>
    public Guid Version { get; set; }
}
