using CleaningSuite.Domain.Services;

namespace CleaningSuite.Application.Services;

public interface IServiceRepository
{
    Task<IReadOnlyList<Service>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<Service?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Service service, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId = null, CancellationToken ct = default);
}
