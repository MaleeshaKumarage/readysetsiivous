using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Notifications;

public sealed class StaffInbox(ITenantSession tenant, TimeProvider clock) : IStaffInbox
{
    public async Task<StaffInboxPage> ListAsync(Guid employeeId, CancellationToken ct = default)
    {
        var items = await tenant.Session.Query<StaffInboxItem>().Where(i => i.EmployeeId == employeeId)
            .OrderByDescending(i => i.CreatedUtc).Take(50).ToListAsync(ct);
        var unread = await tenant.Session.Query<StaffInboxItem>().Where(i => i.EmployeeId == employeeId && i.ReadUtc == null).CountAsync(ct);
        return new(unread, items.Select(i => new StaffInboxEntry(i.Id, i.Kind, i.Title, i.Body, i.Link, i.CreatedUtc, i.ReadUtc.HasValue)).ToList());
    }

    public async Task<bool> MarkReadAsync(Guid employeeId, Guid id, CancellationToken ct = default)
    {
        var item = await tenant.Session.LoadAsync<StaffInboxItem>(id, ct);
        if (item is null || item.EmployeeId != employeeId) return false;
        if (item.ReadUtc is null)
        {
            item.ReadUtc = clock.GetUtcNow().UtcDateTime;
            tenant.Session.Store(item);
            await tenant.Session.SaveChangesAsync(ct);
        }
        return true;
    }
}
