using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Services;
using MediatR;

namespace CleaningSuite.Application.Services.Queries;

public record ListServicesQuery(bool IncludeInactive) : IRequest<IReadOnlyList<Service>>;

public class ListServicesHandler : IRequestHandler<ListServicesQuery, IReadOnlyList<Service>>
{
    private readonly IServiceRepository _services;
    private readonly ITenantCacheService? _cacheService;

    public ListServicesHandler(IServiceRepository services, ITenantCacheService? cacheService = null)
    {
        _services = services;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<Service>> Handle(ListServicesQuery request, CancellationToken ct)
    {
        var fetch = (CancellationToken cToken) => _services.ListAsync(request.IncludeInactive, cToken);
        if (_cacheService is null) return await fetch(ct);
        return (await _cacheService.GetOrAddAsync("services", $"list_{request.IncludeInactive}", fetch, ct: ct))!;
    }
}

public record GetPublicServicesQuery(string Lang) : IRequest<IReadOnlyList<PublicServiceDto>>;

public record PublicServiceDto(
    Guid Id,
    string Slug,
    string Category,
    string Name,
    string Description,
    string? AdditionalInfo,
    string Icon,
    string ImageUrl,
    int SortOrder,
    decimal PriceNet,
    decimal VatRatePercent,
    string Currency);

public class GetPublicServicesHandler : IRequestHandler<GetPublicServicesQuery, IReadOnlyList<PublicServiceDto>>
{
    private readonly IServiceRepository _services;
    private readonly ITenantCacheService? _cacheService;

    public GetPublicServicesHandler(IServiceRepository services, ITenantCacheService? cacheService = null)
    {
        _services = services;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<PublicServiceDto>> Handle(GetPublicServicesQuery request, CancellationToken ct)
    {
        var fetch = async (CancellationToken cToken) =>
        {
            var services = await _services.ListAsync(includeInactive: false, cToken);
            return (IReadOnlyList<PublicServiceDto>)services
                .Select(s => new PublicServiceDto(
                    s.Id,
                    s.Slug,
                    s.Category,
                    s.Name.For(request.Lang) ?? s.Name.For("fi") ?? "",
                    s.Description.For(request.Lang) ?? s.Description.For("fi") ?? "",
                    s.AdditionalInfo?.For(request.Lang) ?? s.AdditionalInfo?.For("fi"),
                    s.Icon,
                    s.ImageUrl,
                    s.SortOrder,
                    s.PriceNet,
                    s.VatRatePercent,
                    s.Currency))
                .ToList();
        };

        if (_cacheService is null) return await fetch(ct);
        return (await _cacheService.GetOrAddAsync("services", $"public_{request.Lang}", fetch, ct: ct))!;
    }
}
