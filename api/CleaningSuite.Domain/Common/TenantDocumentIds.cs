using System.Security.Cryptography;
using System.Text;

namespace CleaningSuite.Domain.Common;

/// <summary>
/// Deterministic ids for singleton-per-tenant documents. Load-by-id is the only
/// Marten path that hydrates concurrency versions, so singletons get fixed ids.
/// </summary>
public static class TenantDocumentIds
{
    public static Guid TenantProfile(string tenantId) => Deterministic($"profile:{tenantId}");
    public static Guid Counter(string tenantId, string name) => Deterministic($"counter:{tenantId}:{name}");

    private static Guid Deterministic(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}
