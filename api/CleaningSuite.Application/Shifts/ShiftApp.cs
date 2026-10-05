using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
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
    DateTime? ValidUntil);

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
    DateTime? ValidUntil = null) : IRequest<ShiftDto>;

public record UpdateShiftCommand(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    string Name,
    ShiftSchedule Schedule,
    string? Notes = null,
    bool IsActive = true,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null) : IRequest<ShiftDto>;

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
        private readonly ITenantCacheService? _cacheService;

        public CreateShiftCommandHandler(
            IShiftRepository repository,
            ICompanyRepository companyRepository,
            IBranchRepository branchRepository,
            ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _companyRepository = companyRepository;
            _branchRepository = branchRepository;
            _cacheService = cacheService;
        }

        public async Task<ShiftDto> Handle(CreateShiftCommand request, CancellationToken ct)
        {
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
                request.ValidUntil);
            await _repository.SaveAsync(shift, ct);
            _cacheService?.RemoveByPrefix("shifts");
            return MapShift(shift);
        }
    }

    public class UpdateShiftCommandHandler : IRequestHandler<UpdateShiftCommand, ShiftDto>
    {
        private readonly IShiftRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public UpdateShiftCommandHandler(IShiftRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
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
                request.ValidUntil);
            await _repository.SaveAsync(shift, ct);
            _cacheService?.RemoveByPrefix("shifts");
            return MapShift(shift);
        }
    }

    public class DeactivateShiftCommandHandler : IRequestHandler<DeactivateShiftCommand>
    {
        private readonly IShiftRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public DeactivateShiftCommandHandler(IShiftRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task Handle(DeactivateShiftCommand request, CancellationToken ct)
        {
            var shift = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Shift", request.Id);
            shift.Deactivate();
            await _repository.SaveAsync(shift, ct);
            _cacheService?.RemoveByPrefix("shifts");
        }
    }

    public class ListShiftsQueryHandler : IRequestHandler<ListShiftsQuery, IReadOnlyList<ShiftDto>>
    {
        private readonly IShiftRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public ListShiftsQueryHandler(IShiftRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task<IReadOnlyList<ShiftDto>> Handle(ListShiftsQuery request, CancellationToken ct)
        {
            var fetch = async (CancellationToken cToken) =>
            {
                var shifts = await _repository.ListAsync(request.CompanyId, request.BranchId, cToken);
                return (IReadOnlyList<ShiftDto>)shifts.Select(MapShift).ToList();
            };

            if (_cacheService is null) return await fetch(ct);

            return (await _cacheService.GetOrAddAsync("shifts", $"list_{request.CompanyId}_{request.BranchId}", fetch, ct: ct))!;
        }
    }

    public class GetShiftQueryHandler : IRequestHandler<GetShiftQuery, ShiftDto>
    {
        private readonly IShiftRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public GetShiftQueryHandler(IShiftRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task<ShiftDto> Handle(GetShiftQuery request, CancellationToken ct)
        {
            var fetch = async (CancellationToken cToken) =>
            {
                var shift = await _repository.GetAsync(request.Id, cToken)
                    ?? throw new NotFoundException("Shift", request.Id);
                return MapShift(shift);
            };

            if (_cacheService is null) return await fetch(ct);

            return (await _cacheService.GetOrAddAsync("shifts", $"get_{request.Id}", fetch, ct: ct))!;
        }
    }

    public class ListShiftAssignmentsQueryHandler : IRequestHandler<ListShiftAssignmentsQuery, IReadOnlyList<ShiftAssignmentDto>>
    {
        private readonly IShiftRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public ListShiftAssignmentsQueryHandler(IShiftRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task<IReadOnlyList<ShiftAssignmentDto>> Handle(ListShiftAssignmentsQuery request, CancellationToken ct)
        {
            var fetch = async (CancellationToken cToken) =>
            {
                var assignments = await _repository.ListAssignmentsByShiftAsync(request.ShiftId, cToken);
                return (IReadOnlyList<ShiftAssignmentDto>)assignments.Select(MapAssignment).ToList();
            };

            if (_cacheService is null) return await fetch(ct);

            return (await _cacheService.GetOrAddAsync("shifts", $"assignments_{request.ShiftId}", fetch, ct: ct))!;
        }
    }

    public class GetShiftOccurrencesQueryHandler : IRequestHandler<GetShiftOccurrencesQuery, IReadOnlyList<ShiftOccurrenceDto>>
    {
        private readonly IShiftRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public GetShiftOccurrencesQueryHandler(IShiftRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task<IReadOnlyList<ShiftOccurrenceDto>> Handle(GetShiftOccurrencesQuery request, CancellationToken ct)
        {
            var from = NormalizeUtc(request.From);
            var to = NormalizeUtc(request.To);

            var fetch = async (CancellationToken cToken) =>
            {
                var shift = await _repository.GetAsync(request.ShiftId, cToken)
                    ?? throw new NotFoundException("Shift", request.ShiftId);
                var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, from, to);
                return (IReadOnlyList<ShiftOccurrenceDto>)occurrences
                    .Select(x => new ShiftOccurrenceDto(NormalizeUtc(x.StartUtc), NormalizeUtc(x.EndUtc)))
                    .ToList();
            };

            if (_cacheService is null) return await fetch(ct);

            return (await _cacheService.GetOrAddAsync("shifts", $"occurrences_{request.ShiftId}_{from:O}_{to:O}", fetch, ct: ct))!;
        }
    }

    public class AssignEmployeeToShiftCommandHandler : IRequestHandler<AssignEmployeeToShiftCommand, ShiftAssignmentDto>
    {
        private const int DefaultMaxConflictWindowDays = 180;

        private readonly IShiftRepository _repository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly ITenantCacheService? _cacheService;
        private readonly int _maxConflictWindowDays;

        public AssignEmployeeToShiftCommandHandler(
            IShiftRepository repository,
            IEmployeeRepository employeeRepository,
            ITenantCacheService? cacheService = null,
            int maxConflictWindowDays = DefaultMaxConflictWindowDays)
        {
            if (maxConflictWindowDays <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConflictWindowDays), maxConflictWindowDays, "Conflict window must be a positive number of days.");
            }

            _repository = repository;
            _employeeRepository = employeeRepository;
            _cacheService = cacheService;
            _maxConflictWindowDays = maxConflictWindowDays;
        }

        public AssignEmployeeToShiftCommandHandler(
            IShiftRepository repository,
            IEmployeeRepository employeeRepository,
            int maxConflictWindowDays)
            : this(repository, employeeRepository, null, maxConflictWindowDays)
        {
        }

        public async Task<ShiftAssignmentDto> Handle(AssignEmployeeToShiftCommand request, CancellationToken ct)
        {
            var shift = await _repository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);

            var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, ct)
                ?? throw new NotFoundException("Employee", request.EmployeeId);

            var utcNow = DateTime.UtcNow;

            var (from, to) = ResolveConflictWindow(shift, utcNow);
            var candidateOccurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, from, to);
            var existingAssignments = await _repository.ListAssignmentsByEmployeeAsync(request.EmployeeId, ct);

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
                _cacheService?.RemoveByPrefix("shifts");
                return MapAssignment(existingAssignment);
            }

            var newAssignment = ShiftAssignment.Create(request.ShiftId, request.EmployeeId, request.Note);
            await _repository.SaveAssignmentAsync(newAssignment, ct);
            _cacheService?.RemoveByPrefix("shifts");
            return MapAssignment(newAssignment);
        }

        private (DateTime From, DateTime To) ResolveConflictWindow(Shift shift, DateTime utcNow)
        {
            var today = utcNow.Date;
            var from = shift.ValidFrom?.Date ?? today;
            if (from < today)
            {
                from = today;
            }

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
        private readonly ITenantCacheService? _cacheService;

        public RemoveEmployeeFromShiftCommandHandler(IShiftRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task Handle(RemoveEmployeeFromShiftCommand request, CancellationToken ct)
        {
            var assignment = await _repository.GetAssignmentAsync(request.ShiftId, request.EmployeeId, ct);
            if (assignment is null)
                return;

            assignment.IsActive = false;
            assignment.UpdatedUtc = DateTime.UtcNow;
            await _repository.SaveAssignmentAsync(assignment, ct);
            _cacheService?.RemoveByPrefix("shifts");
        }
    }

    private static ShiftDto MapShift(Shift shift) =>
        new(shift.Id, shift.CompanyId, shift.BranchId, shift.Name, shift.Schedule, shift.Notes, shift.IsActive, shift.ValidFrom, shift.ValidUntil);

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
            RuleFor(x => x.Schedule).NotNull();
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
            RuleFor(x => x.Schedule).NotNull();
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
