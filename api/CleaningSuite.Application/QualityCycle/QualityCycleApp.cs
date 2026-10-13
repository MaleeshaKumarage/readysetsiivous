using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.QualityCycle;

public record QualityCycleTemplateDto(
    Guid Id,
    Guid? CompanyId,
    Guid? BranchId,
    string Title,
    string? Description,
    List<string> Items,
    bool IsActive,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public record QualityCycleFormItemDto(string ItemText, bool IsChecked);

public record QualityCycleFormDto(
    Guid Id,
    Guid ShiftId,
    string ShiftName,
    Guid EmployeeId,
    string EmployeeName,
    Guid TemplateId,
    string TemplateTitle,
    DateTime ShiftOccurrenceUtc,
    DateTime ShiftOccurrenceEndUtc,
    string Token,
    List<QualityCycleFormItemDto> Items,
    List<string> PhotoUrls,
    string? CleanerNotes,
    bool IsSubmitted,
    DateTime? SubmittedUtc,
    DateTime CreatedUtc,
    string TaskStatus = "Pending");

// Template Commands & Queries
public record CreateQualityCycleTemplateCommand(
    string Title,
    List<string> Items,
    string? Description = null,
    Guid? CompanyId = null,
    Guid? BranchId = null) : IRequest<QualityCycleTemplateDto>;

public record UpdateQualityCycleTemplateCommand(
    Guid Id,
    string Title,
    List<string> Items,
    string? Description = null,
    bool IsActive = true,
    Guid? CompanyId = null,
    Guid? BranchId = null) : IRequest<QualityCycleTemplateDto>;

public record DeleteQualityCycleTemplateCommand(Guid Id) : IRequest;
public record GetQualityCycleTemplateQuery(Guid Id) : IRequest<QualityCycleTemplateDto>;
public record ListQualityCycleTemplatesQuery(Guid? CompanyId = null, Guid? BranchId = null) : IRequest<IReadOnlyList<QualityCycleTemplateDto>>;

// Form Commands & Queries
public record DispatchQualityCycleFormsCommand(Guid? ShiftId = null, DateTime? TargetDateUtc = null) : IRequest<int>;
public record GetQualityCycleFormByTokenQuery(string Token) : IRequest<QualityCycleFormDto>;
public record SubmitQualityCycleFormCommand(
    string Token,
    List<QualityCycleFormItemDto> Items,
    List<string>? PhotoUrls = null,
    string? CleanerNotes = null) : IRequest<QualityCycleFormDto>;

public record ListQualityCycleFormsQuery(Guid? ShiftId = null, DateTime? FromUtc = null, DateTime? ToUtc = null) : IRequest<IReadOnlyList<QualityCycleFormDto>>;
public record GetQualityCycleSummaryPdfQuery(Guid ShiftId, int Year, int Month) : IRequest<byte[]>;

// Handlers
public class QualityCycleHandlers
{
    public class CreateQualityCycleTemplateCommandHandler : IRequestHandler<CreateQualityCycleTemplateCommand, QualityCycleTemplateDto>
    {
        private readonly IQualityCycleRepository _repository;
        public CreateQualityCycleTemplateCommandHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<QualityCycleTemplateDto> Handle(CreateQualityCycleTemplateCommand request, CancellationToken ct)
        {
            var template = QualityCycleTemplate.Create(request.Title, request.Items, request.Description, request.CompanyId, request.BranchId);
            await _repository.SaveTemplateAsync(template, ct);
            return MapTemplate(template);
        }
    }

    public class UpdateQualityCycleTemplateCommandHandler : IRequestHandler<UpdateQualityCycleTemplateCommand, QualityCycleTemplateDto>
    {
        private readonly IQualityCycleRepository _repository;
        public UpdateQualityCycleTemplateCommandHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<QualityCycleTemplateDto> Handle(UpdateQualityCycleTemplateCommand request, CancellationToken ct)
        {
            var template = await _repository.GetTemplateAsync(request.Id, ct)
                ?? throw new NotFoundException("QualityCycleTemplate", request.Id);

            template.Update(request.Title, request.Items, request.Description, request.IsActive, request.CompanyId, request.BranchId);
            await _repository.SaveTemplateAsync(template, ct);
            return MapTemplate(template);
        }
    }

    public class DeleteQualityCycleTemplateCommandHandler : IRequestHandler<DeleteQualityCycleTemplateCommand>
    {
        private readonly IQualityCycleRepository _repository;
        public DeleteQualityCycleTemplateCommandHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task Handle(DeleteQualityCycleTemplateCommand request, CancellationToken ct)
        {
            await _repository.DeleteTemplateAsync(request.Id, ct);
        }
    }

    public class GetQualityCycleTemplateQueryHandler : IRequestHandler<GetQualityCycleTemplateQuery, QualityCycleTemplateDto>
    {
        private readonly IQualityCycleRepository _repository;
        public GetQualityCycleTemplateQueryHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<QualityCycleTemplateDto> Handle(GetQualityCycleTemplateQuery request, CancellationToken ct)
        {
            var template = await _repository.GetTemplateAsync(request.Id, ct)
                ?? throw new NotFoundException("QualityCycleTemplate", request.Id);
            return MapTemplate(template);
        }
    }

    public class ListQualityCycleTemplatesQueryHandler : IRequestHandler<ListQualityCycleTemplatesQuery, IReadOnlyList<QualityCycleTemplateDto>>
    {
        private readonly IQualityCycleRepository _repository;
        public ListQualityCycleTemplatesQueryHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<IReadOnlyList<QualityCycleTemplateDto>> Handle(ListQualityCycleTemplatesQuery request, CancellationToken ct)
        {
            var templates = await _repository.ListTemplatesAsync(request.CompanyId, request.BranchId, ct);
            return templates.Select(MapTemplate).ToList();
        }
    }

    public class DispatchQualityCycleFormsCommandHandler : IRequestHandler<DispatchQualityCycleFormsCommand, int>
    {
        private readonly IShiftRepository _shifts;
        private readonly IQualityCycleDispatcher _dispatcher;
        public DispatchQualityCycleFormsCommandHandler(IShiftRepository shifts, IQualityCycleDispatcher dispatcher)
        { _shifts = shifts; _dispatcher = dispatcher; }

        public async Task<int> Handle(DispatchQualityCycleFormsCommand request, CancellationToken ct)
        {
            if (!request.TargetDateUtc.HasValue)
                return await _dispatcher.EnsureUpcomingAsync(request.ShiftId, ct);
            var date = request.TargetDateUtc.Value.Date;
            IReadOnlyList<Shift> shifts = request.ShiftId.HasValue
                ? (await _shifts.GetAsync(request.ShiftId.Value, ct) is { } selectedShift ? new[] { selectedShift } : Array.Empty<Shift>())
                : await _shifts.ListAsync(null, null, ct);
            var count = 0;
            foreach (var shift in shifts)
                count += await _dispatcher.DispatchAsync(shift,
                    QualityCycleOccurrences.InWindow(shift, date, date.AddDays(1)).Select(x => x.Occurrence).ToList(), ct);
            return count;
        }
    }

    public class GetQualityCycleFormByTokenQueryHandler : IRequestHandler<GetQualityCycleFormByTokenQuery, QualityCycleFormDto>
    {
        private readonly IQualityCycleRepository _repository;
        public GetQualityCycleFormByTokenQueryHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<QualityCycleFormDto> Handle(GetQualityCycleFormByTokenQuery request, CancellationToken ct)
        {
            var form = await _repository.GetFormByTokenAsync(request.Token, ct)
                ?? throw new NotFoundException("QualityCycleForm", Guid.Empty);
            return MapForm(form);
        }
    }

    public class SubmitQualityCycleFormCommandHandler : IRequestHandler<SubmitQualityCycleFormCommand, QualityCycleFormDto>
    {
        private readonly IQualityCycleRepository _repository;
        private readonly TimeProvider _clock;
        public SubmitQualityCycleFormCommandHandler(IQualityCycleRepository repository, TimeProvider? clock = null)
        { _repository = repository; _clock = clock ?? TimeProvider.System; }

        public async Task<QualityCycleFormDto> Handle(SubmitQualityCycleFormCommand request, CancellationToken ct)
        {
            var form = await _repository.GetFormByTokenAsync(request.Token, ct)
                ?? throw new NotFoundException("QualityCycleForm", Guid.Empty);

            if (form.IsSubmitted) throw new QualityCycleConflictException("This shift task has already been completed.");
            if (form.ShiftOccurrenceUtc > _clock.GetUtcNow().UtcDateTime)
                throw new QualityCycleConflictException("This shift task has not started yet.");
            var domainItems = request.Items?.Select(i => new QualityCycleFormItem
            {
                ItemText = i.ItemText,
                IsChecked = i.IsChecked
            }).ToList() ?? new List<QualityCycleFormItem>();

            try { form.Submit(domainItems, request.PhotoUrls, request.CleanerNotes); }
            catch (ArgumentException ex)
            {
                throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("Checklist", ex.Message) });
            }
            await _repository.SaveFormAsync(form, ct);
            return MapForm(form);
        }
    }

    public class ListQualityCycleFormsQueryHandler : IRequestHandler<ListQualityCycleFormsQuery, IReadOnlyList<QualityCycleFormDto>>
    {
        private readonly IQualityCycleRepository _repository;
        public ListQualityCycleFormsQueryHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<IReadOnlyList<QualityCycleFormDto>> Handle(ListQualityCycleFormsQuery request, CancellationToken ct)
        {
            var forms = await _repository.ListFormsAsync(request.ShiftId, request.FromUtc, request.ToUtc, ct);
            return forms.Select(MapForm).ToList();
        }
    }

    public class GetQualityCycleSummaryPdfQueryHandler : IRequestHandler<GetQualityCycleSummaryPdfQuery, byte[]>
    {
        private readonly QualityCycleMonthlyReportService _reports;
        private readonly IQualityCyclePdfGenerator _pdf;
        public GetQualityCycleSummaryPdfQueryHandler(QualityCycleMonthlyReportService reports, IQualityCyclePdfGenerator pdf)
        { _reports = reports; _pdf = pdf; }
        public async Task<byte[]> Handle(GetQualityCycleSummaryPdfQuery request, CancellationToken ct) =>
            _pdf.GenerateMonthlyReportPdf(await _reports.BuildAsync(new(request.Year, request.Month, ShiftId: request.ShiftId), ct));
    }

    private static QualityCycleTemplateDto MapTemplate(QualityCycleTemplate t) =>
        new(t.Id, t.CompanyId, t.BranchId, t.Title, t.Description, t.Items, t.IsActive, t.CreatedUtc, t.UpdatedUtc);

    public static QualityCycleFormDto MapForm(QualityCycleForm f) =>
        new(
            f.Id,
            f.ShiftId,
            f.ShiftName,
            f.EmployeeId,
            f.EmployeeName,
            f.TemplateId,
            f.TemplateTitle,
            f.ShiftOccurrenceUtc,
            f.ShiftOccurrenceEndUtc,
            f.Token,
            f.Items.Select(i => new QualityCycleFormItemDto(i.ItemText, i.IsChecked)).ToList(),
            f.PhotoUrls,
            f.CleanerNotes,
            f.IsSubmitted,
            f.SubmittedUtc,
            f.CreatedUtc,
            f.TaskStatus);
}

public class QualityCycleValidators
{
    public class CreateQualityCycleTemplateCommandValidator : AbstractValidator<CreateQualityCycleTemplateCommand>
    {
        public CreateQualityCycleTemplateCommandValidator()
        {
            RuleFor(x => x.Title).NotEmpty().WithMessage("Template title is required.");
            RuleFor(x => x.Items).NotEmpty().WithMessage("At least one checklist item is required.");
        }
    }

    public class UpdateQualityCycleTemplateCommandValidator : AbstractValidator<UpdateQualityCycleTemplateCommand>
    {
        public UpdateQualityCycleTemplateCommandValidator()
        {
            RuleFor(x => x.Id).NotEqual(Guid.Empty);
            RuleFor(x => x.Title).NotEmpty().WithMessage("Template title is required.");
            RuleFor(x => x.Items).NotEmpty().WithMessage("At least one checklist item is required.");
        }
    }

    public class SubmitQualityCycleFormCommandValidator : AbstractValidator<SubmitQualityCycleFormCommand>
    {
        public SubmitQualityCycleFormCommandValidator()
        {
            RuleFor(x => x.Token).NotEmpty().WithMessage("Form token is required.");
        }
    }
}
