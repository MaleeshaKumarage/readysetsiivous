using System.ComponentModel.DataAnnotations;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Infrastructure.Keycloak;
namespace CleaningSuite.Infrastructure.Leads;

public sealed class LeadAdminDirectory(KeycloakAdminClient keycloak,IEmployeeRepository employees,ITenantContext tenant) : ILeadAdminDirectory
{
    public async Task<IReadOnlyList<LeadAdminRecipient>> ListAsync(CancellationToken ct)
    {
        try {
            var result=new List<LeadAdminRecipient>();
            for(var first=0;first<10000;first+=100) {
                var page=await keycloak.GetRealmRoleUsersAsync(tenant.TenantId,"admin",first,100,ct);
                foreach(var user in page.EnumerateArray()) {
                    if(!user.TryGetProperty("enabled",out var enabled) || !enabled.GetBoolean()) continue;
                    var id=user.GetProperty("id").GetString();var email=user.TryGetProperty("email",out var e)?e.GetString():null;
                    if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(email) || email.Length>254 || !new EmailAddressAttribute().IsValid(email)) continue;
                    var employee=await employees.GetByKeycloakUserIdAsync(id,ct);
                    if(employee is not null && (!employee.IsActive || employee.IdentitySyncPending || employee.Role!="admin")) continue;
                    var name=string.Join(" ",new[]{user.TryGetProperty("firstName",out var f)?f.GetString():null,user.TryGetProperty("lastName",out var l)?l.GetString():null}.Where(x=>!string.IsNullOrWhiteSpace(x)));
                    result.Add(new(id,name.Length>0?name:email!,email!));
                }
                if(page.GetArrayLength()<100) return result.DistinctBy(x=>x.Id).OrderBy(x=>x.Name).ToArray();
            }
            throw new LeadConflictException("The administrator directory is too large to load safely. Contact support.");
        } catch(Exception e) when(e is not OperationCanceledException and not LeadConflictException) {
            throw new LeadConflictException("Administrator recipients could not be verified. Please retry when the identity service is available.");
        }
    }
}
