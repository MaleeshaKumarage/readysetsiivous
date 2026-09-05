using CleaningSuite.Domain.Services;
using MediatR;

namespace CleaningSuite.Application.Services.Queries;

public record ListServicesQuery(bool IncludeInactive) : IRequest<IReadOnlyList<Service>>;

public class ListServicesHandler : IRequestHandler<ListServicesQuery, IReadOnlyList<Service>>
{
    private readonly IServiceRepository _services;

    public ListServicesHandler(IServiceRepository services) => _services = services;

    public Task<IReadOnlyList<Service>> Handle(ListServicesQuery request, CancellationToken ct) =>
        _services.ListAsync(request.IncludeInactive, ct);
}

public record GetPublicServicesQuery(string Lang) : IRequest<IReadOnlyList<PublicServiceDto>>;

public record PublicServiceDto(
    Guid Id,
    string Slug,
    string Category,
    string Name,
    string Description,
    int DurationMinutes,
    decimal PriceNet,
    decimal VatRatePercent,
    string Currency);

public class GetPublicServicesHandler : IRequestHandler<GetPublicServicesQuery, IReadOnlyList<PublicServiceDto>>
{
    private readonly IServiceRepository _services;

    public GetPublicServicesHandler(IServiceRepository services) => _services = services;

    public async Task<IReadOnlyList<PublicServiceDto>> Handle(GetPublicServicesQuery request, CancellationToken ct)
    {
        var services = await _services.ListAsync(includeInactive: false, ct);
        return services
            .Select(s => new PublicServiceDto(
                s.Id,
                s.Slug,
                s.Category,
                s.Name.For(request.Lang) ?? s.Name.For("fi") ?? "",
                s.Description.For(request.Lang) ?? s.Description.For("fi") ?? "",
                s.DurationMinutes,
                s.PriceNet,
                s.VatRatePercent,
                s.Currency))
            .ToList();
    }
}
