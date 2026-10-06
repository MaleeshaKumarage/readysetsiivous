using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Tenants.Commands;

public record UpdateTenantProfileCommand(
    string CompanyName,
    string BusinessId,
    AddressDto CompanyAddress,
    string Email,
    string Phone,
    string BankAccountIBAN,
    string BankBic,
    string TimeZoneId,
    string DefaultLocale,
    decimal DefaultVatRatePercent,
    int PaymentTermsDays,
    bool AllowUnstaffedBookings,
    int MinHoursBeforeBooking) : IRequest<Unit>;

public record AddressDto(string Street, string PostalCode, string City, string? Country);

public class UpdateTenantProfileValidator : AbstractValidator<UpdateTenantProfileCommand>
{
    public UpdateTenantProfileValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.DefaultVatRatePercent).InclusiveBetween(0, 50);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 120);
        RuleFor(x => x.MinHoursBeforeBooking).InclusiveBetween(0, 720);
        RuleFor(x => x.DefaultLocale).Length(2);
    }
}

public class UpdateTenantProfileHandler : IRequestHandler<UpdateTenantProfileCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly ITenantProfileRepository _profiles;

    public UpdateTenantProfileHandler(ITenantContext context, ITenantProfileRepository profiles)
    {
        _context = context;
        _profiles = profiles;
    }

    public async Task<Unit> Handle(UpdateTenantProfileCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var profile = await _profiles.GetAsync(tenantId, ct)
            ?? new TenantProfile { Slug = tenantId, RealmName = tenantId };

        profile.CompanyName = request.CompanyName;
        profile.BusinessId = request.BusinessId;
        profile.CompanyAddress = new Address
        {
            Street = request.CompanyAddress.Street,
            PostalCode = request.CompanyAddress.PostalCode,
            City = request.CompanyAddress.City,
            Country = request.CompanyAddress.Country,
        };
        profile.Email = request.Email;
        profile.Phone = request.Phone;
        profile.BankAccountIBAN = request.BankAccountIBAN;
        profile.BankBic = request.BankBic;
        profile.TimeZoneId = request.TimeZoneId;
        profile.DefaultLocale = request.DefaultLocale;
        profile.DefaultVatRatePercent = request.DefaultVatRatePercent;
        profile.PaymentTermsDays = request.PaymentTermsDays;
        profile.AllowUnstaffedBookings = request.AllowUnstaffedBookings;
        profile.MinHoursBeforeBooking = request.MinHoursBeforeBooking;
        profile.UpdatedUtc = DateTime.UtcNow;

        await _profiles.SaveAsync(tenantId, profile, ct);
        return Unit.Value;
    }
}
