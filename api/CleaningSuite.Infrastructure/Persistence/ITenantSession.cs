using CleaningSuite.Application.Tenants;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// Request-scoped Marten session for the current tenant. The tenant id comes
/// from ITenantContext (set by API middleware from JWT realm or URL slug).
/// Loads through the session track concurrency versions, so update-then-save
/// within one request does a proper optimistic concurrency check.
/// </summary>
public interface ITenantSession
{
    /// <summary>Session bound to the current request's tenant partition.</summary>
    IDocumentSession Session { get; }

    /// <summary>Current request tenant id.</summary>
    string TenantId { get; }
}

public class TenantSessionFactory : ITenantSession, IDisposable
{
    private readonly IDocumentStore _store;
    private readonly ITenantContext _context;
    private IDocumentSession? _session;

    public TenantSessionFactory(IDocumentStore store, ITenantContext context)
    {
        _store = store;
        _context = context;
    }

    // DirtyTrackedSession hydrates IVersioned versions on load and detects
    // mutations, so load-mutate-save in one request does a proper optimistic update.
    public IDocumentSession Session => _session ??= _store.DirtyTrackedSession(_context.TenantId);

    public string TenantId => _context.TenantId;

    public void Dispose() => _session?.Dispose();
}
