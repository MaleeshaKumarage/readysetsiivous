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
    string Token,
    List<QualityCycleFormItemDto> Items,
    List<string> PhotoUrls,
    string? CleanerNotes,
    bool IsSubmitted,
    DateTime? SubmittedUtc,
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
        private readonly IShiftRepository _shiftRepository;
        private readonly IQualityCycleRepository _qcRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IEmailSender _emailSender;

        public DispatchQualityCycleFormsCommandHandler(
            IShiftRepository shiftRepository,
            IQualityCycleRepository qcRepository,
            IEmployeeRepository employeeRepository,
            IEmailSender emailSender)
        {
            _shiftRepository = shiftRepository;
            _qcRepository = qcRepository;
            _employeeRepository = employeeRepository;
            _emailSender = emailSender;
        }

        public async Task<int> Handle(DispatchQualityCycleFormsCommand request, CancellationToken ct)
        {
            var targetDate = (request.TargetDateUtc ?? DateTime.UtcNow).Date;
            var from = targetDate;
            var to = targetDate.AddDays(1).AddSeconds(-1);

            IReadOnlyList<ShiftDto> shifts;
            if (request.ShiftId.HasValue && request.ShiftId.Value != Guid.Empty)
            {
                var shift = await _shiftRepository.GetAsync(request.ShiftId.Value, ct);
                if (shift == null) return 0;
                shifts = new List<ShiftDto> { new ShiftDto(shift.Id, shift.CompanyId, shift.BranchId, shift.Name, shift.Schedule, shift.Notes, shift.IsActive, shift.ValidFrom, shift.ValidUntil, shift.QualityCycleTemplateId) };
            }
            else
            {
                var allShifts = await _shiftRepository.ListAsync(null, null, ct);
                shifts = allShifts.Select(s => new ShiftDto(s.Id, s.CompanyId, s.BranchId, s.Name, s.Schedule, s.Notes, s.IsActive, s.ValidFrom, s.ValidUntil, s.QualityCycleTemplateId)).Where(s => s.IsActive && s.QualityCycleTemplateId.HasValue).ToList();
            }

            int dispatchedCount = 0;

            foreach (var shiftDto in shifts)
            {
                if (!shiftDto.QualityCycleTemplateId.HasValue) continue;

                var template = await _qcRepository.GetTemplateAsync(shiftDto.QualityCycleTemplateId.Value, ct);
                if (template == null || !template.IsActive) continue;

                // Load occurrences for the target date
                var shiftDomain = await _shiftRepository.GetAsync(shiftDto.Id, ct);
                if (shiftDomain == null) continue;

                var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shiftDomain, from, to);
                if (occurrences.Count == 0) continue;

                // Find assigned employees for this shift
                var assignments = await _shiftRepository.ListAssignmentsByEmployeeAsync(Guid.Empty, ct); // Get all or filter
                // Or better, list assignments for shift
                var allAssignments = await _shiftRepository.ListAssignmentsByEmployeeAsync(Guid.Empty, ct);
                var shiftAssignments = allAssignments.Where(a => a.ShiftId == shiftDto.Id && a.IsActive).ToList();

                var employees = new List<CleaningSuite.Domain.Employees.Employee>();
                foreach (var assignment in shiftAssignments)
                {
                    var emp = await _employeeRepository.GetByIdAsync(assignment.EmployeeId, ct);
                    if (emp != null && emp.IsActive)
                    {
                        employees.Add(emp);
                    }
                }

                foreach (var occurrence in occurrences)
                {
                    foreach (var employee in employees)
                    {
                        // Check if form already exists for this occurrence
                        var existingForm = await _qcRepository.GetFormByShiftOccurrenceAsync(shiftDto.Id, employee.Id, occurrence.StartUtc, ct);
                        if (existingForm != null) continue;

                        var form = QualityCycleForm.Create(
                            shiftDto.Id,
                            shiftDto.Name,
                            employee.Id,
                            $"{employee.FirstName} {employee.LastName}",
                            template.Id,
                            template.Title,
                            occurrence.StartUtc,
                            template.Items);

                        await _qcRepository.SaveFormAsync(form, ct);
                        dispatchedCount++;

                        // Send email if employee has email address
                        if (!string.IsNullOrWhiteSpace(employee.Email))
                        {
                            var formUrl = $"https://readysetsiivous.fi/quality-cycle?token={form.Token}";
                            var subject = $"Quality Cycle Checklist: {shiftDto.Name} ({occurrence.StartUtc:yyyy-MM-dd})";
                            var body = $@"Hello {employee.FirstName},

Please complete the Quality Cycle form for your shift '{shiftDto.Name}' on {occurrence.StartUtc:yyyy-MM-dd HH:mm UTC}.

Form Link: {formUrl}

Thank you,
ReadySetSiivous Team";

                            try
                            {
                                await _emailSender.SendAsync(employee.Email, subject, body, ct);
                            }
                            catch
                            {
                                // Email sending failures shouldn't throw out the entire batch
                            }
                        }
                    }
                }
            }

            return dispatchedCount;
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
            f.Token,
            f.Items.Select(i => new QualityCycleFormItemDto(i.ItemText, i.IsChecked)).ToList(),
            f.PhotoUrls,
            f.CleanerNotes,
            f.IsSubmitted,
            f.SubmittedUtc,
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
