using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Tenants;
using MediatR;

namespace CleaningSuite.Application.Tenants.Queries;

public record TenantProfileDto(
    string Slug,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    string CompanyName,
    string BusinessId,
    Address CompanyAddress,
    string Email,
    string Phone,
    string BankAccountIBAN,
    string BankBic,
    string TimeZoneId,
    string DefaultLocale,
    decimal DefaultVatRatePercent,
    int PaymentTermsDays,
    bool AllowUnstaffedBookings,
    int MinHoursBeforeBooking,
    IReadOnlyDictionary<string, LocalizedText> Pages);

public record GetTenantProfileQuery : IRequest<TenantProfileDto?>;

public class GetTenantProfileHandler : IRequestHandler<GetTenantProfileQuery, TenantProfileDto?>
{
    private readonly ITenantContext _context;
    private readonly ITenantProfileRepository _profiles;
    private readonly ITenantCacheService? _cacheService;

    public GetTenantProfileHandler(ITenantContext context, ITenantProfileRepository profiles, ITenantCacheService? cacheService = null)
    {
        _context = context;
        _profiles = profiles;
        _cacheService = cacheService;
    }

    public async Task<TenantProfileDto?> Handle(GetTenantProfileQuery request, CancellationToken ct)
    {
        var fetch = async (CancellationToken cToken) =>
        {
            var p = await _profiles.GetAsync(_context.TenantId, cToken);
            return p is null ? null : MapDto(p);
        };

        if (_cacheService is null) return await fetch(ct);
        return await _cacheService.GetOrAddAsync("tenant", "profile", fetch, ct: ct);
    }

    private static TenantProfileDto MapDto(TenantProfile p) => new(
        p.Slug, p.CreatedUtc, p.UpdatedUtc, p.CompanyName, p.BusinessId, p.CompanyAddress, p.Email, p.Phone,
        p.BankAccountIBAN, p.BankBic, p.TimeZoneId, p.DefaultLocale,
        p.DefaultVatRatePercent, p.PaymentTermsDays, p.AllowUnstaffedBookings,
        p.MinHoursBeforeBooking, p.Pages);
}
