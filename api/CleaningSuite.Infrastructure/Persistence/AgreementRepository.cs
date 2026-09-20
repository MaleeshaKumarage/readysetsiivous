using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class AgreementRepository : IAgreementRepository, IDisposable
{
    private readonly IDocumentStore _store;
    private readonly Dictionary<string, IDocumentSession> _sessions = new();

    public AgreementRepository(IDocumentStore store) => _store = store;

    public async Task<Agreement?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        await Session(tenantId).LoadAsync<Agreement>(id, ct);

    public async Task<IReadOnlyList<Agreement>> ListAsync(string tenantId, CancellationToken ct = default) =>
        (await Session(tenantId).Query<Agreement>().OrderByDescending(a => a.CreatedUtc).ToListAsync(ct));

    public async Task SaveAsync(string tenantId, Agreement agreement, CancellationToken ct = default)
    {
        Session(tenantId).Store(agreement);
        await Session(tenantId).SaveChangesAsync(ct);
    }

    public async Task<Agreement?> FindBySignerTokenAsync(string tenantId, string token, CancellationToken ct = default) =>
        // Marten 8.37 translates `Signers.Any(predicate)` on a JSON child collection into a
        // child-collection filter (CTE); this is the supported form for an element predicate.
        // There is no `Child(...)` LINQ extension in Marten 8.37, so keep this form.
        await Session(tenantId).Query<Agreement>()
            .FirstOrDefaultAsync(a => a.Signers.Any(s => s.Token == token), ct);

    private IDocumentSession Session(string tenantId)
    {
        if (!_sessions.TryGetValue(tenantId, out var session))
        {
            session = _store.DirtyTrackedSession(tenantId);
            _sessions[tenantId] = session;
        }
        return session;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values) session.Dispose();
    }
}
