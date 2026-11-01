using CleaningSuite.Application.Companies;
using CleaningSuite.Domain.Employees;

namespace CleaningSuite.Application.Employees;

/// <summary>Partner membership is tenant-scoped database state, never a client-supplied login claim.</summary>
public static class PartnerCompanyAccess
{
    public static async Task ValidateAsync(string role, Guid? companyId, ICompanyRepository? companies, CancellationToken ct)
    {
        if (role != Employee.RolePartner) return;
        if (companyId is null || companyId == Guid.Empty || companies is null)
            throw new EmployeeIdentityConflictException("Choose an active, partner-enabled company for this partner.");
        var company = await companies.GetAsync(companyId.Value, ct);
        if (company is null || !company.IsActive || !company.IsPartnerEnabled)
            throw new EmployeeIdentityConflictException("The selected company is unavailable or partner access is disabled.");
    }
}
