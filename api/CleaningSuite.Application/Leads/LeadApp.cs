using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace CleaningSuite.Application.Leads;

public record LeadContactPersonDto(string Name, string? Email, string? Phone, string? Role);

public record LeadAttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes);

public record RemovedLeadAttachmentDto(Guid Id, string StorageName);

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
    bool IsActive,
    IReadOnlyList<LeadAttachmentDto> Attachments);

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
    bool? IsActive = null) : IRequest<LeadDto>;

public record DeactivateLeadCommand(Guid Id) : IRequest;
public record ReactivateLeadCommand(Guid Id) : IRequest;
public record DeleteLeadCommand(Guid Id) : IRequest;
public record ListLeadsQuery(string? Search, int Skip, int Take) : IRequest<Paged<LeadDto>>;
public record GetLeadQuery(Guid Id) : IRequest<LeadDto>;

public record AddLeadAttachmentCommand(
    Guid LeadId,
    string FileName,
    string ContentType,
    string StorageName,
    long SizeBytes) : IRequest<LeadAttachmentDto>;

public record RemoveLeadAttachmentCommand(Guid LeadId, Guid AttachmentId) : IRequest<RemovedLeadAttachmentDto>;

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
            // Null optional fields mean "leave existing". Empty strings clear scalars.
            // Attachments are not part of this command, so a profile save cannot drop files.
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

    public sealed class AddLeadAttachmentCommandHandler(ILeadRepository repository) : IRequestHandler<AddLeadAttachmentCommand, LeadAttachmentDto>
    {
        public async Task<LeadAttachmentDto> Handle(AddLeadAttachmentCommand request, CancellationToken ct)
        {
            var lead = await repository.GetAsync(request.LeadId, ct)
                ?? throw new NotFoundException("Lead", request.LeadId);
            if ((lead.Attachments?.Count ?? 0) >= Lead.MaxAttachments)
                throw new ValidationException([new ValidationFailure("File", $"A lead can have at most {Lead.MaxAttachments} files.")]);
            var attachment = lead.AddAttachment(request.FileName, request.ContentType, request.StorageName, request.SizeBytes);
            await repository.SaveAsync(lead, ct);
            return new LeadAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes);
        }
    }

    public sealed class RemoveLeadAttachmentCommandHandler(ILeadRepository repository) : IRequestHandler<RemoveLeadAttachmentCommand, RemovedLeadAttachmentDto>
    {
        public async Task<RemovedLeadAttachmentDto> Handle(RemoveLeadAttachmentCommand request, CancellationToken ct)
        {
            var lead = await repository.GetAsync(request.LeadId, ct)
                ?? throw new NotFoundException("Lead", request.LeadId);
            LeadAttachment attachment;
            try
            {
                attachment = lead.RemoveAttachment(request.AttachmentId);
            }
            catch (InvalidOperationException)
            {
                throw new NotFoundException("Lead attachment", request.AttachmentId);
            }
            await repository.SaveAsync(lead, ct);
            return new RemovedLeadAttachmentDto(attachment.Id, attachment.StorageName);
        }
    }

    private static IEnumerable<LeadContactPerson>? MapPeople(IReadOnlyList<LeadContactPersonDto>? people) =>
        people is null
            ? null
            : people
                .Where(p => p is not null)
                .Select(p => new LeadContactPerson
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
            (lead.ContactPersons ?? []).Select(p => new LeadContactPersonDto(p.Name, p.Email, p.Phone, p.Role)).ToList(),
            lead.Notes,
            lead.IsActive,
            (lead.Attachments ?? []).Select(a => new LeadAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList());
}

public static class LeadValidators
{
    public sealed class CreateLeadCommandValidator : AbstractValidator<CreateLeadCommand>
    {
        public CreateLeadCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Name is mandatory.");
            RuleFor(x => x.BusinessId).MaximumLength(50).When(x => x.BusinessId is not null);
            RuleFor(x => x.MainContactName).MaximumLength(200).When(x => x.MainContactName is not null);
            RuleFor(x => x.MainContactEmail).EmailAddress().MaximumLength(254)
                .When(x => !string.IsNullOrWhiteSpace(x.MainContactEmail));
            RuleFor(x => x.MainContactPhone).MaximumLength(40).When(x => x.MainContactPhone is not null);
            RuleFor(x => x.Notes).MaximumLength(4000).When(x => x.Notes is not null);
            RuleForEach(x => x.Emails).EmailAddress().MaximumLength(254)
                .When(x => x.Emails is not null);
            RuleForEach(x => x.ContactPersons)
                .Must(p => p is not null).WithMessage("Contact person cannot be null.")
                .When(x => x.ContactPersons is not null);
            RuleForEach(x => x.ContactPersons).ChildRules(person =>
            {
                person.RuleFor(p => p!.Name).NotEmpty().MaximumLength(200).When(p => p is not null);
                person.RuleFor(p => p!.Email).EmailAddress().MaximumLength(254)
                    .When(p => p is not null && !string.IsNullOrWhiteSpace(p.Email));
                person.RuleFor(p => p!.Phone).MaximumLength(40).When(p => p is not null && p.Phone is not null);
                person.RuleFor(p => p!.Role).MaximumLength(80).When(p => p is not null && p.Role is not null);
            }).When(x => x.ContactPersons is not null);
        }
    }

    public sealed class UpdateLeadCommandValidator : AbstractValidator<UpdateLeadCommand>
    {
        public UpdateLeadCommandValidator()
        {
            RuleFor(x => x.Id).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Name is mandatory.");
            RuleFor(x => x.BusinessId).MaximumLength(50).When(x => x.BusinessId is not null);
            RuleFor(x => x.MainContactName).MaximumLength(200).When(x => x.MainContactName is not null);
            RuleFor(x => x.MainContactEmail).EmailAddress().MaximumLength(254)
                .When(x => !string.IsNullOrWhiteSpace(x.MainContactEmail));
            RuleFor(x => x.MainContactPhone).MaximumLength(40).When(x => x.MainContactPhone is not null);
            RuleFor(x => x.Notes).MaximumLength(4000).When(x => x.Notes is not null);
            RuleForEach(x => x.Emails).EmailAddress().MaximumLength(254)
                .When(x => x.Emails is not null);
            RuleForEach(x => x.ContactPersons)
                .Must(p => p is not null).WithMessage("Contact person cannot be null.")
                .When(x => x.ContactPersons is not null);
            RuleForEach(x => x.ContactPersons).ChildRules(person =>
            {
                person.RuleFor(p => p!.Name).NotEmpty().MaximumLength(200).When(p => p is not null);
                person.RuleFor(p => p!.Email).EmailAddress().MaximumLength(254)
                    .When(p => p is not null && !string.IsNullOrWhiteSpace(p.Email));
                person.RuleFor(p => p!.Phone).MaximumLength(40).When(p => p is not null && p.Phone is not null);
                person.RuleFor(p => p!.Role).MaximumLength(80).When(p => p is not null && p.Role is not null);
            }).When(x => x.ContactPersons is not null);
        }
    }
}
