using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using Marten;

namespace CleaningSuite.Application.Notifications;

public sealed record NotificationEventItem(string Key, string Group, string Label, string Description, bool App, bool EmailAvailable);
public sealed record NotificationPreferencesResponse(IReadOnlyList<NotificationEventItem> Events);

/// <summary>Lets a person switch the app (inbox and push) on or off for each event that applies to their role.</summary>
public sealed class NotificationPreferenceService(IPartnerStore store)
{
    private IDocumentSession Db => store.Session;

    public async Task<NotificationPreferencesResponse> Get(Employee employee, CancellationToken ct)
    {
        var off = await AppOff(employee.Id, ct);
        return Build(employee, off);
    }

    public async Task<NotificationPreferencesResponse> SetApp(Employee employee, string key, bool enabled, CancellationToken ct)
    {
        var definition = Applicable(employee).FirstOrDefault(e => e.Key == key) ?? throw new NotFoundException("Notification event", Guid.Empty);
        var preference = await Db.LoadAsync<NotificationPreference>(NotificationPreference.IdFor(employee.Id), ct)
            ?? new NotificationPreference { Id = NotificationPreference.IdFor(employee.Id), EmployeeId = employee.Id };
        preference.AppOff = enabled
            ? preference.AppOff.Where(x => x != definition.Key).ToList()
            : preference.AppOff.Append(definition.Key).Distinct().ToList();
        preference.UpdatedUtc = DateTime.UtcNow;
        Db.Store(preference);
        await Db.SaveChangesAsync(ct);
        return Build(employee, preference.AppOff);
    }

    /// <summary>True when the person has switched this event off in the app. Used before anything is queued for them.</summary>
    public static bool AppTurnedOff(NotificationPreference? preference, string kind) =>
        preference is not null && preference.AppOff.Contains(NotificationEvents.Normalize(kind));

    private static IEnumerable<NotificationEvents.Definition> Applicable(Employee employee) =>
        NotificationEvents.For(cleaner: employee.IsActive, staff: employee.Role is Employee.RoleAdmin or Employee.RoleManager);

    private static NotificationPreferencesResponse Build(Employee employee, IReadOnlyCollection<string> off) =>
        new(Applicable(employee).Select(e => new NotificationEventItem(e.Key, e.Group, e.Label, e.Description, !off.Contains(e.Key), e.EmailAvailable)).ToList());

    private async Task<IReadOnlyCollection<string>> AppOff(Guid employeeId, CancellationToken ct) =>
        (await Db.LoadAsync<NotificationPreference>(NotificationPreference.IdFor(employeeId), ct))?.AppOff ?? [];
}
