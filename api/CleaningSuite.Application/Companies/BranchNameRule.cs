using CleaningSuite.Domain.Companies;

namespace CleaningSuite.Application.Companies;

public class BranchNameConflictException(string message) : Exception(message);

/// <summary>
/// Property (branch) names are unique inside one company, ignoring case and extra spaces (#314).
/// Inactive branches count too, so a deactivated property's name cannot be reused by accident.
/// </summary>
public static class BranchNameRule
{
    public static string Normalize(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool Changed(string currentName, string newName) =>
        !Normalize(currentName).Equals(Normalize(newName), StringComparison.OrdinalIgnoreCase);

    public static void EnsureUnique(IEnumerable<Branch> branchesInCompany, string name, Guid? exceptBranchId)
    {
        var wanted = Normalize(name);
        if (branchesInCompany.Any(b => b.Id != exceptBranchId && Normalize(b.Name).Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            throw new BranchNameConflictException($"A property named \"{wanted}\" already exists in this company. Choose another name.");
    }
}
