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
    string TaskStatus = "Pending")
{
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; init; }
}

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

// Employee clock/flow commands & queries
public record StartQualityCycleFormCommand(Guid ShiftId, Guid EmployeeId, DateTime OccurrenceStartUtc, DateTime OccurrenceEndUtc) : IRequest<QualityCycleFormDto>;
public record EndQualityCycleFormCommand(Guid FormId, Guid EmployeeId, List<QualityCycleFormItemDto> Items, List<string>? PhotoUrls = null, string? CleanerNotes = null) : IRequest<QualityCycleFormDto>;
public record MyShiftOccurrenceDto(Guid ShiftId, string ShiftName, DateTime StartUtc, DateTime EndUtc, QualityCycleFormDto? Form);
public record GetMyShiftsQuery(Guid EmployeeId, DateTime FromUtc, DateTime ToUtc) : IRequest<IReadOnlyList<MyShiftOccurrenceDto>>;

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

            RecordActivity(form, request.Items, request.PhotoUrls, request.CleanerNotes, _clock);
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

    public class StartQualityCycleFormCommandHandler : IRequestHandler<StartQualityCycleFormCommand, QualityCycleFormDto>
    {
        private readonly IShiftRepository _shiftRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IQualityCycleRepository _repository;
        private readonly TimeProvider _clock;

        public StartQualityCycleFormCommandHandler(IShiftRepository shiftRepository, IEmployeeRepository employeeRepository, IQualityCycleRepository repository, TimeProvider? clock = null)
        {
            _shiftRepository = shiftRepository;
            _employeeRepository = employeeRepository;
            _repository = repository;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<QualityCycleFormDto> Handle(StartQualityCycleFormCommand request, CancellationToken ct)
        {
            var shift = await _shiftRepository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);
            var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, ct)
                ?? throw new NotFoundException("Employee", request.EmployeeId);

            var start = request.OccurrenceStartUtc.Kind == DateTimeKind.Local ? request.OccurrenceStartUtc.ToUniversalTime() : DateTime.SpecifyKind(request.OccurrenceStartUtc, DateTimeKind.Utc);
            var end = request.OccurrenceEndUtc.Kind == DateTimeKind.Local ? request.OccurrenceEndUtc.ToUniversalTime() : DateTime.SpecifyKind(request.OccurrenceEndUtc, DateTimeKind.Utc);
            if (!shift.IsActive || !employee.IsActive)
                throw new UnauthorizedAccessException();
            if (start > _clock.GetUtcNow().UtcDateTime) throw new QualityCycleConflictException("This shift has not started yet.");
            if (end <= start || end - start > TimeSpan.FromDays(2)
                || !ShiftScheduleCalculator.GenerateOccurrences(shift, start.Date, end.Date.AddDays(1)).Any(o => o.StartUtc == start && o.EndUtc == end))
                throw new QualityCycleConflictException("Choose an actual scheduled shift occurrence.");
            var assignment = await _shiftRepository.GetAssignmentAsync(shift.Id, employee.Id, ct);
            if (assignment == null || !assignment.IsActive || !assignment.Overlaps(start, end))
                throw new UnauthorizedAccessException();

            var form = await _repository.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, start, ct);
            if (form == null)
            {
                var template = shift.QualityCycleTemplateId.HasValue
                    ? await _repository.GetTemplateAsync(shift.QualityCycleTemplateId.Value, ct) : null;
                if (template?.IsActive != true) template = null;
                if (template is not null && ((template.CompanyId.HasValue && template.CompanyId != shift.CompanyId)
                    || (template.BranchId.HasValue && template.BranchId != shift.BranchId)))
                    throw new ShiftConflictException("The checklist does not belong to this shift.");
                form = QualityCycleForm.Create(
                    shift.Id, shift.Name, employee.Id,
                    $"{employee.FirstName} {employee.LastName}",
                    template?.Id ?? Guid.Empty, template?.Title ?? "Shift activities",
                    start, template?.Items ?? new List<string>(), end);
                form.Id = QualityCycleDispatcher.TaskId(shift.Id, employee.Id, start);
                form.CompanyId = shift.CompanyId; form.BranchId = shift.BranchId;
                form.Start();
                if (await _repository.TryCreateFormAsync(form, ct)) return MapForm(form);
                // A simultaneous clock start or worker dispatch owns the same deterministic id.
                form = await _repository.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, start, ct)
                    ?? throw new QualityCycleConflictException("The task changed. Reload your shifts and retry.");
            }

            if (form.IsSubmitted) throw new QualityCycleConflictException("This shift task has already been completed.");
            if (form.StartedAtUtc.HasValue) return MapForm(form);
            form.Start();
            await _repository.SaveFormAsync(form, ct);
            return MapForm(form);
        }
    }

    public class EndQualityCycleFormCommandHandler : IRequestHandler<EndQualityCycleFormCommand, QualityCycleFormDto>
    {
        private readonly IQualityCycleRepository _repository;

        private readonly TimeProvider _clock;
        public EndQualityCycleFormCommandHandler(IQualityCycleRepository repository, TimeProvider? clock = null) { _repository = repository; _clock = clock ?? TimeProvider.System; }

        public async Task<QualityCycleFormDto> Handle(EndQualityCycleFormCommand request, CancellationToken ct)
        {
            var form = await _repository.GetFormAsync(request.FormId, ct)
                ?? throw new NotFoundException("QualityCycleForm", request.FormId);

            if (form.EmployeeId != request.EmployeeId)
                throw new UnauthorizedAccessException();

            if (!form.StartedAtUtc.HasValue)
                throw new QualityCycleConflictException("Start your shift before ending it.");

            RecordActivity(form, request.Items, request.PhotoUrls, request.CleanerNotes, _clock);
            await _repository.SaveFormAsync(form, ct);
            return MapForm(form);
        }
    }

    public class GetMyShiftsQueryHandler : IRequestHandler<GetMyShiftsQuery, IReadOnlyList<MyShiftOccurrenceDto>>
    {
        private readonly IShiftRepository _shiftRepository;
        private readonly IQualityCycleRepository _repository;

        public GetMyShiftsQueryHandler(IShiftRepository shiftRepository, IQualityCycleRepository repository)
        {
            _shiftRepository = shiftRepository;
            _repository = repository;
        }

        public async Task<IReadOnlyList<MyShiftOccurrenceDto>> Handle(GetMyShiftsQuery request, CancellationToken ct)
        {
            var assignments = await _shiftRepository.ListAssignmentsByEmployeeAsync(request.EmployeeId, ct);
            var result = new List<MyShiftOccurrenceDto>();

            foreach (var assignment in assignments)
            {
                if (!assignment.IsActive || assignment.EmployeeId != request.EmployeeId) continue;
                var shift = await _shiftRepository.GetAsync(assignment.ShiftId, ct);
                if (shift == null || !shift.IsActive) continue;

                var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, request.FromUtc, request.ToUtc);
                foreach (var occurrence in occurrences.Where(o => o.StartUtc >= request.FromUtc && o.StartUtc < request.ToUtc && assignment.Overlaps(o.StartUtc, o.EndUtc)))
                {
                    var form = await _repository.GetFormByShiftOccurrenceAsync(shift.Id, request.EmployeeId, occurrence.StartUtc, ct);
                    result.Add(new MyShiftOccurrenceDto(shift.Id, shift.Name, occurrence.StartUtc, occurrence.EndUtc, form == null ? null : MapForm(form)));
                }
            }

            return result.OrderBy(r => r.StartUtc).ToList();
        }
    }

    private static void RecordActivity(QualityCycleForm form, List<QualityCycleFormItemDto>? items, List<string>? photoUrls, string? cleanerNotes, TimeProvider clock)
    {
            if (form.IsSubmitted) throw new QualityCycleConflictException("This shift task has already been completed.");
            if (form.ShiftOccurrenceUtc > clock.GetUtcNow().UtcDateTime)
                throw new QualityCycleConflictException("This shift task has not started yet.");
            var domainItems = items?.Select(i => new QualityCycleFormItem
            {
                ItemText = i.ItemText,
                IsChecked = i.IsChecked
            }).ToList() ?? new List<QualityCycleFormItem>();

            try { form.Submit(domainItems, photoUrls, cleanerNotes); }
            catch (ArgumentException ex)
            {
                throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("Checklist", ex.Message) });
            }
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
            f.TaskStatus) { StartedAtUtc = f.StartedAtUtc, EndedAtUtc = f.EndedAtUtc };
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
