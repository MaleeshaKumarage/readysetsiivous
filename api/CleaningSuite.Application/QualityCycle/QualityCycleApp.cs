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
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    DateTime CreatedUtc);

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
        public SubmitQualityCycleFormCommandHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<QualityCycleFormDto> Handle(SubmitQualityCycleFormCommand request, CancellationToken ct)
        {
            var form = await _repository.GetFormByTokenAsync(request.Token, ct)
                ?? throw new NotFoundException("QualityCycleForm", Guid.Empty);

            var domainItems = request.Items?.Select(i => new QualityCycleFormItem
            {
                ItemText = i.ItemText,
                IsChecked = i.IsChecked
            }).ToList() ?? new List<QualityCycleFormItem>();

            form.Submit(domainItems, request.PhotoUrls, request.CleanerNotes);
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
        private readonly IQualityCycleRepository _repository;
        private readonly IShiftRepository _shiftRepository;
        private readonly Companies.ICompanyRepository _companyRepository;
        private readonly Companies.IBranchRepository _branchRepository;
        private readonly IQualityCyclePdfGenerator _pdfGenerator;

        public GetQualityCycleSummaryPdfQueryHandler(
            IQualityCycleRepository repository,
            IShiftRepository shiftRepository,
            Companies.ICompanyRepository companyRepository,
            Companies.IBranchRepository branchRepository,
            IQualityCyclePdfGenerator pdfGenerator)
        {
            _repository = repository;
            _shiftRepository = shiftRepository;
            _companyRepository = companyRepository;
            _branchRepository = branchRepository;
            _pdfGenerator = pdfGenerator;
        }

        public async Task<byte[]> Handle(GetQualityCycleSummaryPdfQuery request, CancellationToken ct)
        {
            var shift = await _shiftRepository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);

            var company = await _companyRepository.GetAsync(shift.CompanyId, ct);
            var branch = await _branchRepository.GetAsync(shift.BranchId, ct);

            var fromUtc = new DateTime(request.Year, request.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var toUtc = fromUtc.AddMonths(1).AddSeconds(-1);

            var forms = await _repository.ListFormsAsync(request.ShiftId, fromUtc, toUtc, ct);
            var formDtos = forms.Select(MapForm).ToList();

            return _pdfGenerator.GenerateMonthlySummaryPdf(
                shift.Name,
                company?.Name ?? "N/A",
                branch?.Name ?? "N/A",
                request.Year,
                request.Month,
                formDtos);
        }
    }

    public class StartQualityCycleFormCommandHandler : IRequestHandler<StartQualityCycleFormCommand, QualityCycleFormDto>
    {
        private readonly IShiftRepository _shiftRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IQualityCycleRepository _repository;

        public StartQualityCycleFormCommandHandler(IShiftRepository shiftRepository, IEmployeeRepository employeeRepository, IQualityCycleRepository repository)
        {
            _shiftRepository = shiftRepository;
            _employeeRepository = employeeRepository;
            _repository = repository;
        }

        public async Task<QualityCycleFormDto> Handle(StartQualityCycleFormCommand request, CancellationToken ct)
        {
            var shift = await _shiftRepository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);
            if (!shift.QualityCycleTemplateId.HasValue)
                throw new NotFoundException("QualityCycleTemplate", Guid.Empty);

            var template = await _repository.GetTemplateAsync(shift.QualityCycleTemplateId.Value, ct)
                ?? throw new NotFoundException("QualityCycleTemplate", shift.QualityCycleTemplateId.Value);

            var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, ct)
                ?? throw new NotFoundException("Employee", request.EmployeeId);

            var assignment = await _shiftRepository.GetAssignmentAsync(shift.Id, employee.Id, ct);
            if (assignment == null || !assignment.IsActive)
                throw new UnauthorizedAccessException();

            var form = await _repository.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, request.OccurrenceStartUtc, ct);
            if (form == null)
            {
                form = QualityCycleForm.Create(
                    shift.Id, shift.Name, employee.Id,
                    $"{employee.FirstName} {employee.LastName}",
                    template.Id, template.Title,
                    request.OccurrenceStartUtc, template.Items, request.OccurrenceEndUtc);
            }

            form.Start();
            await _repository.SaveFormAsync(form, ct);
            return MapForm(form);
        }
    }

    public class EndQualityCycleFormCommandHandler : IRequestHandler<EndQualityCycleFormCommand, QualityCycleFormDto>
    {
        private readonly IQualityCycleRepository _repository;

        public EndQualityCycleFormCommandHandler(IQualityCycleRepository repository) => _repository = repository;

        public async Task<QualityCycleFormDto> Handle(EndQualityCycleFormCommand request, CancellationToken ct)
        {
            var form = await _repository.GetFormAsync(request.FormId, ct)
                ?? throw new NotFoundException("QualityCycleForm", request.FormId);

            if (form.EmployeeId != request.EmployeeId)
                throw new UnauthorizedAccessException();

            var domainItems = request.Items?.Select(i => new QualityCycleFormItem { ItemText = i.ItemText, IsChecked = i.IsChecked }).ToList()
                ?? new List<QualityCycleFormItem>();

            form.Submit(domainItems, request.PhotoUrls, request.CleanerNotes);
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
                if (!assignment.IsActive) continue;
                var shift = await _shiftRepository.GetAsync(assignment.ShiftId, ct);
                if (shift == null || !shift.IsActive) continue;

                var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, request.FromUtc, request.ToUtc);
                foreach (var occurrence in occurrences)
                {
                    var form = await _repository.GetFormByShiftOccurrenceAsync(shift.Id, request.EmployeeId, occurrence.StartUtc, ct);
                    result.Add(new MyShiftOccurrenceDto(shift.Id, shift.Name, occurrence.StartUtc, occurrence.EndUtc, form == null ? null : MapForm(form)));
                }
            }

            return result.OrderBy(r => r.StartUtc).ToList();
        }
    }

    private static QualityCycleTemplateDto MapTemplate(QualityCycleTemplate t) =>
        new(t.Id, t.CompanyId, t.BranchId, t.Title, t.Description, t.Items, t.IsActive, t.CreatedUtc, t.UpdatedUtc);

    private static QualityCycleFormDto MapForm(QualityCycleForm f) =>
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
            f.StartedAtUtc,
            f.EndedAtUtc,
            f.CreatedUtc);
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
