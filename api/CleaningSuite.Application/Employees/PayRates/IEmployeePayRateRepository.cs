using CleaningSuite.Domain.Employees;

namespace CleaningSuite.Application.Employees.PayRates;

public interface IEmployeePayRateRepository
{
    Task<IReadOnlyList<EmployeePayRate>> ListAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>Stores a new dated rate. Throws <see cref="EmployeePayRateConflictException"/> when that date already has a rate.</summary>
    Task AddAsync(EmployeePayRate entry, CancellationToken ct = default);
}

public sealed class EmployeePayRateConflictException(string message) : Exception(message);
