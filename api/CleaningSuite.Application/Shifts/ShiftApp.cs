using CleaningSuite.Application.Common;
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
    Task SaveAsync(Shift shift, CancellationToken ct = default);
    Task<ShiftAssignment?> GetAssignmentAsync(Guid shiftId, Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftAssignment>> ListAssignmentsByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
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

public record GetShiftOccurrencesQuery(Guid ShiftId, DateTime From, DateTime To) : IRequest<IReadOnlyList<ShiftOccurrenceDto>>;

public record AssignEmployeeToShiftCommand(Guid ShiftId, Guid EmployeeId, string? Note = null) : IRequest<ShiftAssignmentDto>;

public record RemoveEmployeeFromShiftCommand(Guid ShiftId, Guid EmployeeId) : IRequest;

public class ShiftHandlers
{
    public class CreateShiftCommandHandler : IRequestHandler<CreateShiftCommand, ShiftDto>
    {
        private readonly IShiftRepository _repository;

        public CreateShiftCommandHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task<ShiftDto> Handle(CreateShiftCommand request, CancellationToken ct)
        {
            var shift = Shift.Create(
                request.CompanyId,
                request.BranchId,
                request.Name,
                request.Schedule,
                request.Notes,
                request.ValidFrom,
                request.ValidUntil);
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
                request.ValidUntil);
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
            var shift = await _repository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);
            var occurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, request.From, request.To);
            return occurrences.Select(x => new ShiftOccurrenceDto(x.StartUtc, x.EndUtc)).ToList();
        }
    }

    public class AssignEmployeeToShiftCommandHandler : IRequestHandler<AssignEmployeeToShiftCommand, ShiftAssignmentDto>
    {
        private readonly IShiftRepository _repository;
        private readonly DateTime _from = DateTime.Today;
        private readonly DateTime _to = DateTime.Today.AddDays(60);

        public AssignEmployeeToShiftCommandHandler(IShiftRepository repository)
        {
            _repository = repository;
        }

        public async Task<ShiftAssignmentDto> Handle(AssignEmployeeToShiftCommand request, CancellationToken ct)
        {
            var shift = await _repository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);

            var candidateOccurrences = ShiftScheduleCalculator.GenerateOccurrences(shift, _from, _to);
            var existingAssignments = await _repository.ListAssignmentsByEmployeeAsync(request.EmployeeId, ct);

            foreach (var assignment in existingAssignments)
            {
                var existingShift = await _repository.GetAsync(assignment.ShiftId, ct);
                if (existingShift is not null && existingShift.Id != request.ShiftId)
                {
                    var existingOccurrences = ShiftScheduleCalculator.GenerateOccurrences(existingShift, _from, _to);
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
                existingAssignment.AssignedAtUtc = DateTime.UtcNow;
                existingAssignment.UpdatedUtc = DateTime.UtcNow;
                await _repository.SaveAssignmentAsync(existingAssignment, ct);
                return MapAssignment(existingAssignment);
            }

            var newAssignment = ShiftAssignment.Create(request.ShiftId, request.EmployeeId, request.Note);
            await _repository.SaveAssignmentAsync(newAssignment, ct);
            return MapAssignment(newAssignment);
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
            await _repository.DeleteAssignmentAsync(assignment.Id, ct);
        }
    }

    private static ShiftDto MapShift(Shift shift) =>
        new(shift.Id, shift.CompanyId, shift.BranchId, shift.Name, shift.Schedule, shift.Notes, shift.IsActive, shift.ValidFrom, shift.ValidUntil);

    private static ShiftAssignmentDto MapAssignment(ShiftAssignment assignment) =>
        new(assignment.Id, assignment.ShiftId, assignment.EmployeeId, assignment.AssignedAtUtc, assignment.IsActive, assignment.Note);
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
