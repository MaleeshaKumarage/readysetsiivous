using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Services;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Services.Commands;

public record ServiceFields(
    string Slug,
    string Category,
    Dictionary<string, string> Name,
    Dictionary<string, string> Description,
    int DurationMinutes,
    decimal PriceNet,
    decimal VatRatePercent,
    string Currency,
    bool IsFeatured,
    int SortOrder);

public record CreateServiceCommand(ServiceFields Fields) : IRequest<Guid>;

public record UpdateServiceCommand(Guid Id, ServiceFields Fields, bool IsActive) : IRequest<Unit>;

public record SoftDeleteServiceCommand(Guid Id) : IRequest<Unit>;

public class ServiceFieldsValidator : AbstractValidator<ServiceFields>
{
    public ServiceFieldsValidator()
    {
        RuleFor(x => x.Slug).Matches(@"^[a-z0-9][a-z0-9-]{1,60}$");
        RuleFor(x => x.Name).NotEmpty().Must(d => d.ContainsKey("fi")).WithMessage("Name must include fi");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 1440);
        RuleFor(x => x.PriceNet).GreaterThanOrEqualTo(0);
        RuleFor(x => x.VatRatePercent).InclusiveBetween(0, 50);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10000);
    }
}

public class CreateServiceValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceValidator() => RuleFor(x => x.Fields).SetValidator(new ServiceFieldsValidator());
}

public class UpdateServiceValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceValidator() => RuleFor(x => x.Fields).SetValidator(new ServiceFieldsValidator());
}

public class CreateServiceHandler : IRequestHandler<CreateServiceCommand, Guid>
{
    private readonly IServiceRepository _services;

    public CreateServiceHandler(IServiceRepository services) => _services = services;

    public async Task<Guid> Handle(CreateServiceCommand request, CancellationToken ct)
    {
        if (await _services.SlugExistsAsync(request.Fields.Slug, ct: ct))
            throw new SlugConflictException(request.Fields.Slug);

        var service = new Service();
        Apply(service, request.Fields);

        await _services.SaveAsync(service, ct);
        return service.Id;
    }

    internal static void Apply(Service service, ServiceFields fields)
    {
        service.Slug = fields.Slug;
        service.Category = fields.Category;
        service.Name = new LocalizedText { Values = fields.Name };
        service.Description = new LocalizedText { Values = fields.Description };
        service.DurationMinutes = fields.DurationMinutes;
        service.PriceNet = fields.PriceNet;
        service.VatRatePercent = fields.VatRatePercent;
        service.Currency = fields.Currency;
        service.IsFeatured = fields.IsFeatured;
        service.SortOrder = fields.SortOrder;
        service.UpdatedUtc = DateTime.UtcNow;
    }
}

public class UpdateServiceHandler : IRequestHandler<UpdateServiceCommand, Unit>
{
    private readonly IServiceRepository _services;

    public UpdateServiceHandler(IServiceRepository services) => _services = services;

    public async Task<Unit> Handle(UpdateServiceCommand request, CancellationToken ct)
    {
        var service = await _services.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Service", request.Id);

        if (await _services.SlugExistsAsync(request.Fields.Slug, request.Id, ct))
            throw new SlugConflictException(request.Fields.Slug);

        CreateServiceHandler.Apply(service, request.Fields);
        service.IsActive = request.IsActive;

        await _services.SaveAsync(service, ct);
        return Unit.Value;
    }
}

public class SoftDeleteServiceHandler : IRequestHandler<SoftDeleteServiceCommand, Unit>
{
    private readonly IServiceRepository _services;

    public SoftDeleteServiceHandler(IServiceRepository services) => _services = services;

    public async Task<Unit> Handle(SoftDeleteServiceCommand request, CancellationToken ct)
    {
        var service = await _services.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Service", request.Id);

        service.IsActive = false;
        service.UpdatedUtc = DateTime.UtcNow;
        await _services.SaveAsync(service, ct);
        return Unit.Value;
    }
}
