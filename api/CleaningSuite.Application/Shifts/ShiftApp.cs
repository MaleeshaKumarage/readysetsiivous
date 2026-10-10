using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Shifts;

public record ShiftDto(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    string Name,
    ShiftSchedule Schedule,
    string? Notes,
    bool IsActive,
    DateTime? ValidFrom,
    DateTime? ValidUntil,
    Guid? QualityCycleTemplateId = null);

public record ShiftAssignmentDto(
    Guid Id,
    Guid ShiftId,
    Guid EmployeeId,
    DateTime AssignedAtUtc,
    bool IsActive,
    string? Note);

public record ShiftOccurrenceDto(DateTime StartUtc, DateTime EndUtc);

public interface IShiftRepository
{
    Task<Shift?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Shift>> ListAsync(Guid? companyId, Guid? branchId, CancellationToken ct = default);
    Task<IReadOnlyList<Shift>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task SaveAsync(Shift shift, CancellationToken ct = default);
    Task<ShiftAssignment?> GetAssignmentAsync(Guid shiftId, Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftAssignment>> ListAssignmentsByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftAssignment>> ListAssignmentsByShiftAsync(Guid shiftId, CancellationToken ct = default);
    Task SaveAssignmentAsync(ShiftAssignment assignment, CancellationToken ct = default);
    Task DeleteAssignmentAsync(Guid assignmentId, CancellationToken ct = default);
}

public class ShiftConflictException : Exception
{
    public ShiftConflictException(string message) : base(message) { }
}

public record CreateShiftCommand(
    Guid CompanyId,
    Guid BranchId,
    string Name,
    ShiftSchedule Schedule,
    string? Notes = null,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    Guid? QualityCycleTemplateId = null) : IRequest<ShiftDto>;

public record UpdateShiftCommand(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    string Name,
    ShiftSchedule Schedule,
    string? Notes = null,
    bool IsActive = true,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    Guid? QualityCycleTemplateId = null) : IRequest<ShiftDto>;

public record DeactivateShiftCommand(Guid Id) : IRequest;

public record ListShiftsQuery(Guid? CompanyId, Guid? BranchId) : IRequest<IReadOnlyList<ShiftDto>>;

public record GetShiftQuery(Guid Id) : IRequest<ShiftDto>;

public record ListShiftAssignmentsQuery(Guid ShiftId) : IRequest<IReadOnlyList<ShiftAssignmentDto>>;

public record GetShiftOccurrencesQuery(Guid ShiftId, DateTime From, DateTime To) : IRequest<IReadOnlyList<ShiftOccurrenceDto>>;

public record AssignEmployeeToShiftCommand(Guid ShiftId, Guid EmployeeId, string? Note = null) : IRequest<ShiftAssignmentDto>;

public record RemoveEmployeeFromShiftCommand(Guid ShiftId, Guid EmployeeId) : IRequest;

public class ShiftHandlers
{
    public class CreateShiftCommandHandler : IRequestHandler<CreateShiftCommand, ShiftDto>
    {
        private readonly IShiftRepository _repository;
        private readonly ICompanyRepository _companyRepository;
        private readonly IBranchRepository _branchRepository;

        public CreateShiftCommandHandler(
            IShiftRepository repository,
            ICompanyRepository companyRepository,
            IBranchRepository branchRepository)
        {
            _repository = repository;
            _companyRepository = companyRepository;
            _branchRepository = branchRepository;
        }

        public async Task<ShiftDto> Handle(CreateShiftCommand request, CancellationToken ct)
        {
            // Ensure the referenced parent entities exist before persisting the shift, so the
            // admin API cannot create a shift pointing at a non-existent company or branch.
            _ = await _companyRepository.GetAsync(request.CompanyId, ct)
                ?? throw new NotFoundException("Company", request.CompanyId);

            var branch = await _branchRepository.GetAsync(request.BranchId, ct)
                ?? throw new NotFoundException("Branch", request.BranchId);
            if (branch.CompanyId != request.CompanyId)
            {
                throw new NotFoundException("Branch", request.BranchId);
            }

            var shift = Shift.Create(
                request.CompanyId,
                request.BranchId,
                request.Name,
                request.Schedule,
                request.Notes,
                request.ValidFrom,
                request.ValidUntil,
                request.QualityCycleTemplateId);
            await _repository.SaveAsync(shift, ct);
            return MapShift(shift);
        }
    }

    public class UpdateShiftCommandHandler : IRequestHandler<UpdateShiftCommand, ShiftDto>
    {
        private readonly IShiftRepository _repository;

        public UpdateShiftCommandHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task<ShiftDto> Handle(UpdateShiftCommand request, CancellationToken ct)
        {
            var shift = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Shift", request.Id);
            shift.Update(
                request.CompanyId,
                request.BranchId,
                request.Name,
                request.Schedule,
                request.Notes,
                request.IsActive,
                request.ValidFrom,
                request.ValidUntil,
                request.QualityCycleTemplateId);
            await _repository.SaveAsync(shift, ct);
            return MapShift(shift);
        }
    }

    public class DeactivateShiftCommandHandler : IRequestHandler<DeactivateShiftCommand>
    {
        private readonly IShiftRepository _repository;

        public DeactivateShiftCommandHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task Handle(DeactivateShiftCommand request, CancellationToken ct)
        {
            var shift = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Shift", request.Id);
            shift.Deactivate();
            await _repository.SaveAsync(shift, ct);
        }
    }

    public class ListShiftsQueryHandler : IRequestHandler<ListShiftsQuery, IReadOnlyList<ShiftDto>>
    {
        private readonly IShiftRepository _repository;

        public ListShiftsQueryHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task<IReadOnlyList<ShiftDto>> Handle(ListShiftsQuery request, CancellationToken ct)
        {
            var shifts = await _repository.ListAsync(request.CompanyId, request.BranchId, ct);
            return shifts.Select(MapShift).ToList();
        }
    }

    public class ListShiftAssignmentsQueryHandler : IRequestHandler<ListShiftAssignmentsQuery, IReadOnlyList<ShiftAssignmentDto>>
    {
        private readonly IShiftRepository _repository;

        public ListShiftAssignmentsQueryHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task<IReadOnlyList<ShiftAssignmentDto>> Handle(ListShiftAssignmentsQuery request, CancellationToken ct)
        {
            var assignments = await _repository.ListAssignmentsByShiftAsync(request.ShiftId, ct);
            return assignments.Select(MapAssignment).ToList();
        }
    }

    public class GetShiftQueryHandler : IRequestHandler<GetShiftQuery, ShiftDto>
    {
        private readonly IShiftRepository _repository;

        public GetShiftQueryHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task<ShiftDto> Handle(GetShiftQuery request, CancellationToken ct)
        {
            var shift = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Shift", request.Id);
            return MapShift(shift);
        }
    }

    public class GetShiftOccurrencesQueryHandler : IRequestHandler<GetShiftOccurrencesQuery, IReadOnlyList<ShiftOccurrenceDto>>
    {
        private readonly IShiftRepository _repository;

        public GetShiftOccurrencesQueryHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task<IReadOnlyList<ShiftOccurrenceDto>> Handle(GetShiftOccurrencesQuery request, CancellationToken ct)
        {
            // Query-string DateTime binding yields DateTimeKind.Unspecified. Treat the caller's
            // input as UTC explicitly so occurrence generation is anchored unambiguously.
            var from = NormalizeUtc(request.From);
            var to = NormalizeUtc(request.To);

            var shift = await _repository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);
            var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, from, to);
            return occurrences
                .Select(x => new ShiftOccurrenceDto(NormalizeUtc(x.StartUtc), NormalizeUtc(x.EndUtc)))
                .ToList();
        }
    }

    public class AssignEmployeeToShiftCommandHandler : IRequestHandler<AssignEmployeeToShiftCommand, ShiftAssignmentDto>
    {
        // Default upper bound (in days) on the window used when checking for overlapping
        // assignments. Only the shift's own ValidFrom/ValidUntil define the window; when those
        // are open-ended this cap prevents generating an unbounded set of occurrences. It is a
        // constructor parameter so callers/tests can supply an explicit policy instead of relying
        // on a hidden constant.
        private const int DefaultMaxConflictWindowDays = 180;

        private readonly IShiftRepository _repository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IQualityCycleRepository _qcRepository;
        private readonly IEmailSender _emailSender;
        private readonly int _maxConflictWindowDays;

        public AssignEmployeeToShiftCommandHandler(
            IShiftRepository repository,
            IEmployeeRepository employeeRepository,
            IQualityCycleRepository qcRepository,
            IEmailSender emailSender,
            int maxConflictWindowDays = DefaultMaxConflictWindowDays)
        {
            if (maxConflictWindowDays <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConflictWindowDays), maxConflictWindowDays, "Conflict window must be a positive number of days.");
            }

            _repository = repository;
            _employeeRepository = employeeRepository;
            _qcRepository = qcRepository;
            _emailSender = emailSender;
            _maxConflictWindowDays = maxConflictWindowDays;
        }

        public async Task<ShiftAssignmentDto> Handle(AssignEmployeeToShiftCommand request, CancellationToken ct)
        {
            var shift = await _repository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);

            // Resolve the employee before doing anything else, so a typo or stale id fails fast
            // instead of persisting a ShiftAssignment that points at a non-existent employee.
            var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, ct)
                ?? throw new NotFoundException("Employee", request.EmployeeId);

            // Capture "now" inside Handle (not in a field initializer) so it stays correct
            // regardless of handler lifetime and is anchored to UTC rather than server-local time.
            var utcNow = DateTime.UtcNow;

            var (from, to) = ResolveConflictWindow(shift, utcNow);
            var candidateOccurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, from, to);
            var existingAssignments = await _repository.ListAssignmentsByEmployeeAsync(request.EmployeeId, ct);

            // Load every other shift the employee is assigned to in a single query instead of
            // issuing a GetAsync per assignment, and generate each shift's occurrences just once.
            var otherShiftIds = existingAssignments
                .Select(a => a.ShiftId)
                .Where(id => id != request.ShiftId)
                .Distinct()
                .ToList();

            if (otherShiftIds.Count > 0)
            {
                var otherShifts = await _repository.ListByIdsAsync(otherShiftIds, ct);
                foreach (var existingShift in otherShifts)
                {
                    var existingOccurrences = ShiftScheduleCalculator.GenerateOccurrences(existingShift, from, to);
                    if (ShiftScheduleCalculator.HasOverlap(candidateOccurrences, existingOccurrences))
                    {
                        throw new ShiftConflictException($"Employee is already assigned to an overlapping shift.");
                    }
                }
            }

            var existingAssignment = await _repository.GetAssignmentAsync(request.ShiftId, request.EmployeeId, ct);
            if (existingAssignment is not null)
            {
                existingAssignment.IsActive = true;
                existingAssignment.Note = request.Note;
                existingAssignment.AssignedAtUtc = utcNow;
                existingAssignment.UpdatedUtc = utcNow;
                await _repository.SaveAssignmentAsync(existingAssignment, ct);
                await DispatchQualityCycleFormAsync(shift, employee, utcNow, ct);
                return MapAssignment(existingAssignment);
            }

            var newAssignment = ShiftAssignment.Create(request.ShiftId, request.EmployeeId, request.Note);
            await _repository.SaveAssignmentAsync(newAssignment, ct);
            await DispatchQualityCycleFormAsync(shift, employee, utcNow, ct);
            return MapAssignment(newAssignment);
        }

        /// <summary>
        /// If the shift has a quality-cycle template, create (or reuse) the form for the
        /// employee's next shift occurrence and email them the fill-in link.
        /// </summary>
        private async Task DispatchQualityCycleFormAsync(Shift shift, Employee employee, DateTime utcNow, CancellationToken ct)
        {
            if (!shift.QualityCycleTemplateId.HasValue) return;

            var template = await _qcRepository.GetTemplateAsync(shift.QualityCycleTemplateId.Value, ct);
            if (template == null || !template.IsActive) return;

            var from = utcNow;
            var to = utcNow.AddDays(30);
            var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, from, to);
            if (occurrences.Count == 0) return;
            var occurrence = occurrences[0];

            var existingForm = await _qcRepository.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, occurrence.StartUtc, ct);
            if (existingForm != null) return;

            var form = QualityCycleForm.Create(
                shift.Id,
                shift.Name,
                employee.Id,
                $"{employee.FirstName} {employee.LastName}",
                template.Id,
                template.Title,
                occurrence.StartUtc,
                template.Items);
            await _qcRepository.SaveFormAsync(form, ct);

            if (!string.IsNullOrWhiteSpace(employee.Email))
            {
                var formUrl = $"https://readysetsiivous.fi/quality-cycle?token={form.Token}";
                var subject = $"Quality Cycle Checklist: {shift.Name} ({occurrence.StartUtc:yyyy-MM-dd})";
                var body = $@"Hello {employee.FirstName},

Please complete the Quality Cycle form for your shift '{shift.Name}' on {occurrence.StartUtc:yyyy-MM-dd HH:mm UTC}.

Form Link: {formUrl}

Thank you,
ReadySetSiivous Team";

                try
                {
                    await _emailSender.SendAsync(employee.Email, subject, body, ct);
                }
                catch
                {
                    // Email failures must not roll back the assignment.
                }
            }
        }

        private (DateTime From, DateTime To) ResolveConflictWindow(Shift shift, DateTime utcNow)
        {
            // Anchor the window on the UTC date supplied by the caller, not server-local time, so
            // the generated occurrences line up with the *Utc values used elsewhere and don't
            // drift with the host timezone or DST.
            var today = utcNow.Date;
            var from = shift.ValidFrom?.Date ?? today;
            if (from < today)
            {
                from = today;
            }

            // Derive the end of the window from the shift's validity, capped by the explicit
            // policy so open-ended shifts (no ValidUntil) don't generate an unbounded number of
            // occurrences.
            var maxTo = from.AddDays(_maxConflictWindowDays);
            var to = shift.ValidUntil?.Date ?? maxTo;
            if (to > maxTo)
            {
                to = maxTo;
            }
            else if (to < from)
            {
                to = from;
            }

            return (from, to);
        }
    }

    public class RemoveEmployeeFromShiftCommandHandler : IRequestHandler<RemoveEmployeeFromShiftCommand>
    {
        private readonly IShiftRepository _repository;

        public RemoveEmployeeFromShiftCommandHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task Handle(RemoveEmployeeFromShiftCommand request, CancellationToken ct)
        {
            var assignment = await _repository.GetAssignmentAsync(request.ShiftId, request.EmployeeId, ct);
            if (assignment is null)
                return;

            // Soft-deactivate rather than hard-delete so removal is auditable and consistent with
            // the IsActive pattern used elsewhere (and which ListAssignmentsByEmployeeAsync filters
            // on). The row is retained; only its active flag is flipped.
            assignment.IsActive = false;
            assignment.UpdatedUtc = DateTime.UtcNow;
            await _repository.SaveAssignmentAsync(assignment, ct);
        }
    }

    private static ShiftDto MapShift(Shift shift) =>
        new(shift.Id, shift.CompanyId, shift.BranchId, shift.Name, shift.Schedule, shift.Notes, shift.IsActive, shift.ValidFrom, shift.ValidUntil, shift.QualityCycleTemplateId);

    private static ShiftAssignmentDto MapAssignment(ShiftAssignment assignment) =>
        new(assignment.Id, assignment.ShiftId, assignment.EmployeeId, assignment.AssignedAtUtc, assignment.IsActive, assignment.Note);

    // Occurrence DateTimes are composed as `date.Date + TimeSpan` from an Unspecified source,
    // so re-tag the result as UTC. Local values are converted; already-UTC values pass through.
    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

public class ShiftValidators
{
    public class CreateShiftCommandValidator : AbstractValidator<CreateShiftCommand>
    {
        public CreateShiftCommandValidator()
        {
            RuleFor(x => x.CompanyId).NotEqual(Guid.Empty);
            RuleFor(x => x.BranchId).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().WithMessage("Shift name is mandatory.");
            RuleFor(x => x.Schedule).NotNull().Custom((schedule, context) =>
            {
                if (schedule is null) return;
                try
                {
                    schedule.Validate();
                }
                catch (Exception ex)
                {
                    context.AddFailure("Schedule", ex.Message);
                }
            });
        }
    }

    public class UpdateShiftCommandValidator : AbstractValidator<UpdateShiftCommand>
    {
        public UpdateShiftCommandValidator()
        {
            RuleFor(x => x.Id).NotEqual(Guid.Empty);
            RuleFor(x => x.CompanyId).NotEqual(Guid.Empty);
            RuleFor(x => x.BranchId).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().WithMessage("Shift name is mandatory.");
            RuleFor(x => x.Schedule).NotNull().Custom((schedule, context) =>
            {
                if (schedule is null) return;
                try
                {
                    schedule.Validate();
                }
                catch (Exception ex)
                {
                    context.AddFailure("Schedule", ex.Message);
                }
            });
        }
    }

    public class AssignEmployeeToShiftCommandValidator : AbstractValidator<AssignEmployeeToShiftCommand>
    {
        public AssignEmployeeToShiftCommandValidator()
        {
            RuleFor(x => x.ShiftId).NotEqual(Guid.Empty);
            RuleFor(x => x.EmployeeId).NotEqual(Guid.Empty);
        }
    }
}
