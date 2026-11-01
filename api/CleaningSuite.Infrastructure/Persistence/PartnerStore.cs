using CleaningSuite.Application.Partners;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public sealed class PartnerStore(ITenantSession tenant) : IPartnerStore
{
    public IDocumentSession Session => tenant.Session;
}
