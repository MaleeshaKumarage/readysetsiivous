using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Services;
using MediatR;

namespace CleaningSuite.Application.Services.Queries;

public record ServiceDto(
    Guid Id,
    string Slug,
    string Category,
    LocalizedText Name,
    LocalizedText Description,
    LocalizedText? AdditionalInfo,
    int DurationMinutes,
    decimal PriceNet,
    decimal VatRatePercent,
    string Currency,
    bool IsActive,
    bool IsFeatured,
    int SortOrder,
    string Icon,
    string ImageUrl);

public record ListServicesQuery(bool IncludeInactive) : IRequest<IReadOnlyList<ServiceDto>>;

public class ListServicesHandler : IRequestHandler<ListServicesQuery, IReadOnlyList<ServiceDto>>
{
    private readonly IServiceRepository _services;
    private readonly ITenantCacheService? _cacheService;

    public ListServicesHandler(IServiceRepository services, ITenantCacheService? cacheService = null)
    {
        _services = services;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<ServiceDto>> Handle(ListServicesQuery request, CancellationToken ct)
    {
        var fetch = async (CancellationToken cToken) =>
        {
            var list = await _services.ListAsync(request.IncludeInactive, cToken);
            return (IReadOnlyList<ServiceDto>)list.Select(MapDto).ToList();
        };

        if (_cacheService is null) return await fetch(ct);
        return (await _cacheService.GetOrAddAsync("services", $"list_{request.IncludeInactive}", fetch, ct: ct))!;
    }

    private static ServiceDto MapDto(Service s) => new(
        s.Id, s.Slug, s.Category, s.Name, s.Description, s.AdditionalInfo,
        s.DurationMinutes, s.PriceNet, s.VatRatePercent, s.Currency,
        s.IsActive, s.IsFeatured, s.SortOrder, s.Icon, s.ImageUrl);
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
        var lang = SanitizeLang(request.Lang);
        var fetch = async (CancellationToken cToken) =>
        {
            var services = await _services.ListAsync(includeInactive: false, cToken);
            return (IReadOnlyList<PublicServiceDto>)services
                .Select(s => new PublicServiceDto(
                    s.Id,
                    s.Slug,
                    s.Category,
                    s.Name.For(lang) ?? s.Name.For("fi") ?? "",
                    s.Description.For(lang) ?? s.Description.For("fi") ?? "",
                    s.AdditionalInfo?.For(lang) ?? s.AdditionalInfo?.For("fi"),
                    s.Icon,
                    s.ImageUrl,
                    s.SortOrder,
                    s.PriceNet,
                    s.VatRatePercent,
                    s.Currency))
                .ToList();
        };

        if (_cacheService is null) return await fetch(ct);
        return (await _cacheService.GetOrAddAsync("services", $"public_{lang}", fetch, ct: ct))!;
    }

    private static string SanitizeLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "fi";
        var clean = new string(lang.Where(char.IsLetterOrDigit).Take(5).ToArray()).ToLowerInvariant();
        return string.IsNullOrEmpty(clean) ? "fi" : clean;
    }
}
