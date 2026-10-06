using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Common;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Tenants.Commands;

public record UpdateTenantPageCommand(string PageKey, Dictionary<string, string> Values) : IRequest<Unit>;

public class UpdateTenantPageValidator : AbstractValidator<UpdateTenantPageCommand>
{
    public UpdateTenantPageValidator()
    {
        RuleFor(x => x.PageKey).Matches(@"^[a-z][a-z0-9-]{1,40}$");
        RuleFor(x => x.Values).NotEmpty().Must(d => d.ContainsKey("fi")).WithMessage("Values must include fi");
    }
}

public class UpdateTenantPageHandler : IRequestHandler<UpdateTenantPageCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly ITenantProfileRepository _profiles;
    private readonly ITenantCacheService? _cacheService;

    public UpdateTenantPageHandler(ITenantContext context, ITenantProfileRepository profiles, ITenantCacheService? cacheService = null)
    {
        _context = context;
        _profiles = profiles;
        _cacheService = cacheService;
    }

    public async Task<Unit> Handle(UpdateTenantPageCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var profile = await _profiles.GetAsync(tenantId, ct)
            ?? throw new NotFoundException("TenantProfile", Guid.Empty);

        profile.Pages[request.PageKey] = new LocalizedText { Values = request.Values };
        profile.UpdatedUtc = DateTime.UtcNow;

        await _profiles.SaveAsync(tenantId, profile, ct);
        _cacheService?.RemoveByPrefix("tenant");
        return Unit.Value;
    }
}
