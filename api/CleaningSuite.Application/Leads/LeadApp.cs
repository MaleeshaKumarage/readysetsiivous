using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Leads;

public record LeadContactPersonDto(string Name, string? Email, string? Phone, string? Role);

public record LeadDto(
    Guid Id,
    string Name,
    string? BusinessId,
    string? MainContactName,
    string? MainContactEmail,
    string? MainContactPhone,
    IReadOnlyList<string> Emails,
    IReadOnlyList<LeadContactPersonDto> ContactPersons,
    string? Notes,
    bool IsActive);

public interface ILeadRepository
{
    Task<Lead?> GetAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Lead> Items, int Total)> ListAsync(string? search, int skip, int take, CancellationToken ct = default);
    Task SaveAsync(Lead lead, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public record CreateLeadCommand(
    string Name,
    string? BusinessId = null,
    string? MainContactName = null,
    string? MainContactEmail = null,
    string? MainContactPhone = null,
    IReadOnlyList<string>? Emails = null,
    IReadOnlyList<LeadContactPersonDto>? ContactPersons = null,
    string? Notes = null) : IRequest<LeadDto>;

public record UpdateLeadCommand(
    Guid Id,
    string Name,
    string? BusinessId = null,
    string? MainContactName = null,
    string? MainContactEmail = null,
    string? MainContactPhone = null,
    IReadOnlyList<string>? Emails = null,
    IReadOnlyList<LeadContactPersonDto>? ContactPersons = null,
    string? Notes = null,
    bool IsActive = true) : IRequest<LeadDto>;

public record DeactivateLeadCommand(Guid Id) : IRequest;
public record ReactivateLeadCommand(Guid Id) : IRequest;
public record DeleteLeadCommand(Guid Id) : IRequest;
public record ListLeadsQuery(string? Search, int Skip, int Take) : IRequest<Paged<LeadDto>>;
public record GetLeadQuery(Guid Id) : IRequest<LeadDto>;

public static class LeadHandlers
{
    public sealed class CreateLeadCommandHandler(ILeadRepository repository) : IRequestHandler<CreateLeadCommand, LeadDto>
    {
        public async Task<LeadDto> Handle(CreateLeadCommand request, CancellationToken ct)
        {
            var lead = Lead.Create(
                request.Name,
                request.BusinessId,
                request.MainContactName,
                request.MainContactEmail,
                request.MainContactPhone,
                request.Emails,
                MapPeople(request.ContactPersons),
                request.Notes);
            await repository.SaveAsync(lead, ct);
            return Map(lead);
        }
    }

    public sealed class UpdateLeadCommandHandler(ILeadRepository repository) : IRequestHandler<UpdateLeadCommand, LeadDto>
    {
        public async Task<LeadDto> Handle(UpdateLeadCommand request, CancellationToken ct)
        {
            var lead = await repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Lead", request.Id);
            lead.Update(
                request.Name,
                request.BusinessId,
                request.MainContactName,
                request.MainContactEmail,
                request.MainContactPhone,
                request.Emails,
                MapPeople(request.ContactPersons),
                request.Notes,
                request.IsActive);
            await repository.SaveAsync(lead, ct);
            return Map(lead);
        }
    }

    public sealed class DeactivateLeadCommandHandler(ILeadRepository repository) : IRequestHandler<DeactivateLeadCommand>
    {
        public async Task Handle(DeactivateLeadCommand request, CancellationToken ct)
        {
            var lead = await repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Lead", request.Id);
            lead.Deactivate();
            await repository.SaveAsync(lead, ct);
        }
    }

    public sealed class ReactivateLeadCommandHandler(ILeadRepository repository) : IRequestHandler<ReactivateLeadCommand>
    {
        public async Task Handle(ReactivateLeadCommand request, CancellationToken ct)
        {
            var lead = await repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Lead", request.Id);
            lead.Reactivate();
            await repository.SaveAsync(lead, ct);
        }
    }

    public sealed class DeleteLeadCommandHandler(ILeadRepository repository) : IRequestHandler<DeleteLeadCommand>
    {
        public async Task Handle(DeleteLeadCommand request, CancellationToken ct)
        {
            var lead = await repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Lead", request.Id);
            if (lead.IsActive)
                throw new InactiveRecordDeleteConflictException("Deactivate the lead before deleting it.");
            await repository.DeleteAsync(lead.Id, ct);
        }
    }

    public sealed class ListLeadsQueryHandler(ILeadRepository repository) : IRequestHandler<ListLeadsQuery, Paged<LeadDto>>
    {
        public async Task<Paged<LeadDto>> Handle(ListLeadsQuery request, CancellationToken ct)
        {
            var (items, total) = await repository.ListAsync(request.Search, request.Skip, request.Take, ct);
            return new Paged<LeadDto>(items.Select(Map).ToList(), total);
        }
    }

    public sealed class GetLeadQueryHandler(ILeadRepository repository) : IRequestHandler<GetLeadQuery, LeadDto>
    {
        public async Task<LeadDto> Handle(GetLeadQuery request, CancellationToken ct)
        {
            var lead = await repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Lead", request.Id);
            return Map(lead);
        }
    }

    private static IEnumerable<LeadContactPerson>? MapPeople(IReadOnlyList<LeadContactPersonDto>? people) =>
        people?.Select(p => new LeadContactPerson
        {
            Name = p.Name,
            Email = p.Email,
            Phone = p.Phone,
            Role = p.Role
        });

    public static LeadDto Map(Lead lead) =>
        new(
            lead.Id,
            lead.Name,
            lead.BusinessId,
            lead.MainContactName,
            lead.MainContactEmail,
            lead.MainContactPhone,
            lead.Emails,
            lead.ContactPersons.Select(p => new LeadContactPersonDto(p.Name, p.Email, p.Phone, p.Role)).ToList(),
            lead.Notes,
            lead.IsActive);
}

public static class LeadValidators
{
    public sealed class CreateLeadCommandValidator : AbstractValidator<CreateLeadCommand>
    {
        public CreateLeadCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is mandatory.");
            RuleFor(x => x.MainContactEmail).EmailAddress().MaximumLength(254)
                .When(x => !string.IsNullOrWhiteSpace(x.MainContactEmail));
            RuleForEach(x => x.Emails).EmailAddress().MaximumLength(254)
                .When(x => x.Emails is not null);
            RuleForEach(x => x.ContactPersons).ChildRules(person =>
            {
                person.RuleFor(p => p.Name).NotEmpty().When(p => p is not null);
                person.RuleFor(p => p.Email).EmailAddress().MaximumLength(254)
                    .When(p => !string.IsNullOrWhiteSpace(p.Email));
            }).When(x => x.ContactPersons is not null);
        }
    }

    public sealed class UpdateLeadCommandValidator : AbstractValidator<UpdateLeadCommand>
    {
        public UpdateLeadCommandValidator()
        {
            RuleFor(x => x.Id).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is mandatory.");
            RuleFor(x => x.MainContactEmail).EmailAddress().MaximumLength(254)
                .When(x => !string.IsNullOrWhiteSpace(x.MainContactEmail));
            RuleForEach(x => x.Emails).EmailAddress().MaximumLength(254)
                .When(x => x.Emails is not null);
            RuleForEach(x => x.ContactPersons).ChildRules(person =>
            {
                person.RuleFor(p => p.Name).NotEmpty().When(p => p is not null);
                person.RuleFor(p => p.Email).EmailAddress().MaximumLength(254)
                    .When(p => !string.IsNullOrWhiteSpace(p.Email));
            }).When(x => x.ContactPersons is not null);
        }
    }
}
