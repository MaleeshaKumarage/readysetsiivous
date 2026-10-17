using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
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
    public IReadOnlyList<QualityCyclePhoto> Photos { get; init; } = Array.Empty<QualityCyclePhoto>();
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
public record SetQualityCycleTemplateActiveCommand(Guid Id, bool Active) : IRequest;
public record GetQualityCycleTemplateQuery(Guid Id) : IRequest<QualityCycleTemplateDto>;
public record ListQualityCycleTemplatesQuery(Guid? CompanyId = null, Guid? BranchId = null) : IRequest<IReadOnlyList<QualityCycleTemplateDto>>;

// Form Commands & Queries
public record DispatchQualityCycleFormsCommand(Guid? ShiftId = null, DateTime? TargetDateUtc = null) : IRequest<int>;
public record GetQualityCycleFormByTokenQuery(string Token) : IRequest<QualityCycleFormDto>;
public record SubmitQualityCycleFormCommand(
    string Token,
    List<QualityCycleFormItemDto> Items,
    List<string>? PhotoUrls = null,
    string? CleanerNotes = null, List<QualityCyclePhoto>? Photos = null) : IRequest<QualityCycleFormDto>;

public record ListQualityCycleFormsQuery(Guid? ShiftId = null, DateTime? FromUtc = null, DateTime? ToUtc = null) : IRequest<IReadOnlyList<QualityCycleFormDto>>;
public record GetQualityCycleSummaryPdfQuery(Guid ShiftId, int Year, int Month) : IRequest<byte[]>;

// Employee clock/flow commands & queries
public record StartQualityCycleFormCommand(Guid ShiftId, Guid EmployeeId, DateTime OccurrenceStartUtc, DateTime OccurrenceEndUtc) : IRequest<QualityCycleFormDto>;
public record EndQualityCycleFormCommand(Guid FormId, Guid EmployeeId, List<QualityCycleFormItemDto> Items, List<string>? PhotoUrls = null, string? CleanerNotes = null, List<QualityCyclePhoto>? Photos = null) : IRequest<QualityCycleFormDto>;
public record MyShiftOccurrenceDto(Guid ShiftId, string ShiftName, DateTime StartUtc, DateTime EndUtc, QualityCycleFormDto? Form, string TimeZoneId = "Europe/Helsinki", bool IsDeleted = false);
public record GetMyShiftsQuery(Guid EmployeeId, DateTime FromUtc, DateTime ToUtc) : IRequest<IReadOnlyList<MyShiftOccurrenceDto>>;

// Handlers
public class QualityCycleHandlers
{
    private static async Task ValidateTemplateScopeAsync(ICompanyRepository companies, IBranchRepository branches,
        Guid? companyId, Guid? branchId, CancellationToken ct)
    {
        if (companyId.HasValue)
            _ = await companies.GetAsync(companyId.Value, ct) ?? throw new NotFoundException("Company", companyId.Value);
        if (branchId.HasValue)
        {
            var branch = await branches.GetAsync(branchId.Value, ct) ?? throw new NotFoundException("Branch", branchId.Value);
            if (companyId.HasValue && branch.CompanyId != companyId.Value)
                throw new NotFoundException("Branch", branchId.Value);
            if (!companyId.HasValue)
                _ = await companies.GetAsync(branch.CompanyId, ct) ?? throw new NotFoundException("Company", branch.CompanyId);
        }
    }

    public class CreateQualityCycleTemplateCommandHandler : IRequestHandler<CreateQualityCycleTemplateCommand, QualityCycleTemplateDto>
    {
        private readonly IQualityCycleRepository _repository;
        private readonly ICompanyRepository _companies;
        private readonly IBranchRepository _branches;
        public CreateQualityCycleTemplateCommandHandler(IQualityCycleRepository repository, ICompanyRepository companies, IBranchRepository branches)
        { _repository = repository; _companies = companies; _branches = branches; }

        public async Task<QualityCycleTemplateDto> Handle(CreateQualityCycleTemplateCommand request, CancellationToken ct)
        {
            await ValidateTemplateScopeAsync(_companies, _branches, request.CompanyId, request.BranchId, ct);
            var template = QualityCycleTemplate.Create(request.Title, request.Items, request.Description, request.CompanyId, request.BranchId);
            await _repository.SaveTemplateAsync(template, ct);
            return MapTemplate(template);
        }
    }

    public class UpdateQualityCycleTemplateCommandHandler : IRequestHandler<UpdateQualityCycleTemplateCommand, QualityCycleTemplateDto>
    {
        private readonly IQualityCycleRepository _repository;
        private readonly ICompanyRepository _companies;
        private readonly IBranchRepository _branches;
        public UpdateQualityCycleTemplateCommandHandler(IQualityCycleRepository repository, ICompanyRepository companies, IBranchRepository branches)
        { _repository = repository; _companies = companies; _branches = branches; }

        public async Task<QualityCycleTemplateDto> Handle(UpdateQualityCycleTemplateCommand request, CancellationToken ct)
        {
            var template = await _repository.GetTemplateAsync(request.Id, ct)
                ?? throw new NotFoundException("QualityCycleTemplate", request.Id);

            await ValidateTemplateScopeAsync(_companies, _branches, request.CompanyId, request.BranchId, ct);
            template.Update(request.Title, request.Items, request.Description, request.IsActive, request.CompanyId, request.BranchId);
            await _repository.SaveTemplateAsync(template, ct);
            return MapTemplate(template);
        }
    }

    public class SetQualityCycleTemplateActiveCommandHandler(IQualityCycleRepository repository, ICompanyRepository companies, IBranchRepository branches) : IRequestHandler<SetQualityCycleTemplateActiveCommand>
    {
        public async Task Handle(SetQualityCycleTemplateActiveCommand request, CancellationToken ct)
        {
            var template = await repository.GetTemplateAsync(request.Id, ct) ?? throw new NotFoundException("QualityCycleTemplate", request.Id);
            if (request.Active)
                await ValidateTemplateScopeAsync(companies, branches, template.CompanyId, template.BranchId, ct);
            template.IsActive = request.Active;
            template.UpdatedUtc = DateTime.UtcNow;
            await repository.SaveTemplateAsync(template, ct);
        }
    }

    public class DeleteQualityCycleTemplateCommandHandler : IRequestHandler<DeleteQualityCycleTemplateCommand>
    {
        private readonly IQualityCycleRepository _repository;
        private readonly IShiftRepository _shifts;
        public DeleteQualityCycleTemplateCommandHandler(IQualityCycleRepository repository, IShiftRepository shifts)
        { _repository = repository; _shifts = shifts; }

        public async Task Handle(DeleteQualityCycleTemplateCommand request, CancellationToken ct)
        {
            var template = await _repository.GetTemplateAsync(request.Id, ct)
                ?? throw new NotFoundException("QualityCycleTemplate", request.Id);
            if (template.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the checklist template before deleting it.");
            if ((await _shifts.ListForReportingAsync(ct)).Any(s => s.QualityCycleTemplateId == template.Id
                    || s.QualityCyclePlans.Any(p => p.TemplateId == template.Id))
                || (await _repository.ListFormsAsync(null, null, null, ct, null, template.Id, null, null)).Count > 0)
                throw new InactiveRecordDeleteConflictException("This template is referenced by a shift or checklist history and must be retained.");
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
        private readonly ITenantProfileRepository? _profiles;
        private readonly ITenantContext? _tenant;
        public DispatchQualityCycleFormsCommandHandler(IShiftRepository shifts, IQualityCycleDispatcher dispatcher,
            ITenantProfileRepository? profiles = null, ITenantContext? tenant = null)
        { _shifts = shifts; _dispatcher = dispatcher; _profiles = profiles; _tenant = tenant; }

        public async Task<int> Handle(DispatchQualityCycleFormsCommand request, CancellationToken ct)
        {
            if (!request.TargetDateUtc.HasValue)
                return await _dispatcher.EnsureUpcomingAsync(request.ShiftId, ct);
            var date = request.TargetDateUtc.Value.Date;
            var (_, zone) = await TenantTimeZone.LoadAsync(_profiles, _tenant, ct);
            IReadOnlyList<Shift> shifts = request.ShiftId.HasValue
                ? (await _shifts.GetAsync(request.ShiftId.Value, ct) is { } selectedShift ? new[] { selectedShift } : Array.Empty<Shift>())
                : await _shifts.ListAsync(null, null, ct);
            var count = 0;
            foreach (var shift in shifts)
                count += await _dispatcher.DispatchAsync(shift,
                    QualityCycleOccurrences.InWindow(shift, date, date.AddDays(1), zone).Select(x => x.Occurrence).ToList(), ct);
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
        private readonly IUploadContentValidator _uploads;
        public SubmitQualityCycleFormCommandHandler(IQualityCycleRepository repository, IUploadContentValidator uploads, TimeProvider? clock = null)
        { _repository = repository; _uploads = uploads; _clock = clock ?? TimeProvider.System; }

        public async Task<QualityCycleFormDto> Handle(SubmitQualityCycleFormCommand request, CancellationToken ct)
        {
            var form = await _repository.GetFormByTokenAsync(request.Token, ct)
                ?? throw new NotFoundException("QualityCycleForm", Guid.Empty);

            _uploads.ValidateQualityCyclePhotos(request.Photos, request.PhotoUrls);
            RecordActivity(form, request.Items, request.PhotoUrls, request.CleanerNotes, _clock, request.Photos);
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
        private readonly ITenantProfileRepository? _profiles;
        private readonly ITenantContext? _tenant;
        private readonly IShiftWorkReservation? _reservation;

        public StartQualityCycleFormCommandHandler(IShiftRepository shiftRepository, IEmployeeRepository employeeRepository, IQualityCycleRepository repository, TimeProvider? clock = null,
            ITenantProfileRepository? profiles = null, ITenantContext? tenant = null, IShiftWorkReservation? reservation = null)
        {
            _shiftRepository = shiftRepository;
            _employeeRepository = employeeRepository;
            _repository = repository;
            _clock = clock ?? TimeProvider.System;
            _profiles = profiles;
            _tenant = tenant;
            _reservation = reservation;
        }

        public async Task<QualityCycleFormDto> Handle(StartQualityCycleFormCommand request, CancellationToken ct)
        {
            await using var lease = _reservation is null ? null : await _reservation.AcquireAsync(ct);
            var shift = await _shiftRepository.GetAsync(request.ShiftId, ct)
                ?? throw new NotFoundException("Shift", request.ShiftId);
            var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, ct)
                ?? throw new NotFoundException("Employee", request.EmployeeId);

            var start = request.OccurrenceStartUtc.Kind == DateTimeKind.Local ? request.OccurrenceStartUtc.ToUniversalTime() : DateTime.SpecifyKind(request.OccurrenceStartUtc, DateTimeKind.Utc);
            var end = request.OccurrenceEndUtc.Kind == DateTimeKind.Local ? request.OccurrenceEndUtc.ToUniversalTime() : DateTime.SpecifyKind(request.OccurrenceEndUtc, DateTimeKind.Utc);
            if (!shift.IsActive || !employee.IsActive)
                throw new UnauthorizedAccessException();
            if (end <= start || end - start > TimeSpan.FromDays(2))
                throw new QualityCycleConflictException("Choose an actual scheduled shift occurrence.");
            var (_, zone) = await TenantTimeZone.LoadAsync(_profiles, _tenant, ct);
            var (localFrom, localTo) = TenantTimeZone.LocalDatesForUtcRange(start, end, zone);
            var exceptionFrom = TenantTimeZone.StartOfLocalDay(localFrom, zone);
            var exceptionTo = TenantTimeZone.StartOfLocalDay(localTo.AddDays(1), zone);
            var regular = await _shiftRepository.GetAssignmentAsync(shift.Id, employee.Id, ct);
            var resolved = await _shiftRepository.ResolveRosterAsync(shift, exceptionFrom, exceptionTo, zone, ct,
                fallbackAssignments: regular is null ? null : new[] { regular });
            var matching = resolved.Where(o => o.Occurrence.StartUtc == start && o.Occurrence.EndUtc == end).ToList();
            if (matching.Count == 0)
                throw new QualityCycleConflictException("Choose an actual scheduled shift occurrence.");
            // An extra visit can intentionally share the same time window as a scheduled
            // occurrence. Prefer the occurrence explicitly assigned to this employee, then
            // fall back to the regular shift assignment, instead of throwing on duplicates.
            if (!matching.Any(o => o.EmployeeIds.Contains(employee.Id))) throw new UnauthorizedAccessException();
            if (TenantTimeZone.LocalDate(start, zone) > TenantTimeZone.LocalDate(_clock.GetUtcNow().UtcDateTime, zone))
                throw new QualityCycleConflictException("Future shifts cannot be started before their scheduled date.");

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
                form.Start(_clock.GetUtcNow().UtcDateTime);
                if (_reservation is not null)
                {
                    await _repository.CreateStartedFormAsync(form, ct);
                    return MapForm(form);
                }
                if (await _repository.TryCreateFormAsync(form, ct)) return MapForm(form);
                // A simultaneous clock start or worker dispatch owns the same deterministic id.
                form = await _repository.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, start, ct)
                    ?? throw new QualityCycleConflictException("The task changed. Reload your shifts and retry.");
            }

            if (form.EmployeeId != employee.Id || form.ShiftId != shift.Id || form.ShiftOccurrenceUtc != start)
                throw new UnauthorizedAccessException();
            if (form.IsSubmitted) throw new QualityCycleConflictException("This shift task has already been completed.");
            if (form.StartedAtUtc.HasValue) return MapForm(form);
            form.Start(_clock.GetUtcNow().UtcDateTime);
            await _repository.SaveFormAsync(form, ct);
            return MapForm(form);
        }
    }

    public class EndQualityCycleFormCommandHandler : IRequestHandler<EndQualityCycleFormCommand, QualityCycleFormDto>
    {
        private readonly IQualityCycleRepository _repository;
        private readonly IUploadContentValidator _uploads;

        private readonly TimeProvider _clock;
        public EndQualityCycleFormCommandHandler(IQualityCycleRepository repository, IUploadContentValidator uploads, TimeProvider? clock = null) { _repository = repository; _uploads = uploads; _clock = clock ?? TimeProvider.System; }

        public async Task<QualityCycleFormDto> Handle(EndQualityCycleFormCommand request, CancellationToken ct)
        {
            var form = await _repository.GetFormAsync(request.FormId, ct)
                ?? throw new NotFoundException("QualityCycleForm", request.FormId);

            if (form.EmployeeId != request.EmployeeId)
                throw new UnauthorizedAccessException();

            if (!form.StartedAtUtc.HasValue)
                throw new QualityCycleConflictException("Start your shift before ending it.");

            _uploads.ValidateQualityCyclePhotos(request.Photos, request.PhotoUrls);
            RecordActivity(form, request.Items, request.PhotoUrls, request.CleanerNotes, _clock, request.Photos);
            await _repository.SaveFormAsync(form, ct);
            return MapForm(form);
        }
    }

    public class GetMyShiftsQueryHandler : IRequestHandler<GetMyShiftsQuery, IReadOnlyList<MyShiftOccurrenceDto>>
    {
        private readonly IShiftRepository _shiftRepository;
        private readonly IQualityCycleRepository _repository;
        private readonly ITenantProfileRepository? _profiles;
        private readonly ITenantContext? _tenant;
        private readonly TimeProvider _clock;

        public GetMyShiftsQueryHandler(IShiftRepository shiftRepository, IQualityCycleRepository repository,
            ITenantProfileRepository? profiles = null, ITenantContext? tenant = null, TimeProvider? clock = null)
        {
            _shiftRepository = shiftRepository;
            _repository = repository;
            _profiles = profiles;
            _tenant = tenant;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<IReadOnlyList<MyShiftOccurrenceDto>> Handle(GetMyShiftsQuery request, CancellationToken ct)
        {
            var from = request.FromUtc.Kind == DateTimeKind.Local ? request.FromUtc.ToUniversalTime() : DateTime.SpecifyKind(request.FromUtc, DateTimeKind.Utc);
            var to = request.ToUtc.Kind == DateTimeKind.Local ? request.ToUtc.ToUniversalTime() : DateTime.SpecifyKind(request.ToUtc, DateTimeKind.Utc);
            if (to <= from || to - from > TimeSpan.FromDays(32) || to.Date >= DateTime.MaxValue.Date)
                throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("range", "Choose an increasing UTC interval of at most 32 days within supported calendar bounds.") });
            var (zoneId, zone) = await TenantTimeZone.LoadAsync(_profiles, _tenant, ct);
            var (localFrom, localTo) = TenantTimeZone.LocalDatesForUtcRange(from, to, zone);
            var currentAssignments = await _shiftRepository.ListAssignmentsByEmployeeAsync(request.EmployeeId, ct);
            var history = await _shiftRepository.ListAssignmentHistoryByEmployeeAsync(request.EmployeeId, ct) ?? Array.Empty<ShiftAssignment>();
            var assignments = (currentAssignments ?? Array.Empty<ShiftAssignment>()).Concat(history)
                .Where(a => a.EmployeeId == request.EmployeeId).GroupBy(a => a.Id).Select(g => g.First()).ToList();
            var employeeExceptions = await _shiftRepository.ListOccurrenceExceptionsByEmployeeAsync(request.EmployeeId, from, to, ct)
                ?? Array.Empty<ShiftOccurrenceException>();
            var individualAssignments = await _shiftRepository.ListOccurrenceAssignmentsByEmployeeAsync(request.EmployeeId, ct)
                ?? Array.Empty<ShiftOccurrenceAssignment>();
            var shiftIds = assignments.Select(a => a.ShiftId).Concat(employeeExceptions.Select(x => x.ShiftId))
                .Concat(individualAssignments.Select(x => x.ShiftId)).Distinct().ToArray();
            var result = new List<MyShiftOccurrenceDto>();
            var liveShifts = new Dictionary<Guid, Shift?>();

            foreach (var shiftId in shiftIds)
            {
                var shift = await _shiftRepository.GetAsync(shiftId, ct);
                liveShifts[shiftId] = shift;
                if (shift == null || shift.Id != shiftId || !shift.IsActive) continue;

                var occurrences = await _shiftRepository.ResolveRosterAsync(shift, from, to, zone, ct, fallbackAssignments: assignments);
                foreach (var entry in occurrences.Where(o => o.EmployeeIds.Contains(request.EmployeeId)))
                {
                    var occurrence = entry.Occurrence;
                    var form = await _repository.GetFormByShiftOccurrenceAsync(shift.Id, request.EmployeeId, occurrence.StartUtc, ct);
                    if (form is not null && (form.EmployeeId != request.EmployeeId || form.ShiftId != shift.Id || form.ShiftOccurrenceUtc != occurrence.StartUtc))
                        form = null;
                    result.Add(new MyShiftOccurrenceDto(shift.Id, shift.Name, occurrence.StartUtc, occurrence.EndUtc, form == null ? null : MapForm(form), zoneId));
                }
            }

            // Preserve personal history after roster changes only while the shift is active.
            // Deleted/inactive evidence remains available through administrative reports.
            var recorded = await _repository.ListFormsAsync(null, from, to, ct, request.EmployeeId, null, null, null)
                ?? Array.Empty<QualityCycleForm>();
            foreach (var form in recorded.Where(f => f.EmployeeId == request.EmployeeId
                && f.ShiftOccurrenceUtc >= from && f.ShiftOccurrenceUtc < to
                && (f.IsSubmitted || f.StartedAtUtc.HasValue)))
            {
                if (!liveShifts.TryGetValue(form.ShiftId, out var liveShift))
                {
                    liveShift = await _shiftRepository.GetAsync(form.ShiftId, ct);
                    liveShifts[form.ShiftId] = liveShift;
                }
                if (liveShift is not { IsActive: true } || form.ShiftDeletedUtc.HasValue) continue;
                var index = result.FindIndex(x => x.ShiftId == form.ShiftId && x.StartUtc == form.ShiftOccurrenceUtc);
                var occurrence = new MyShiftOccurrenceDto(form.ShiftId, form.ShiftName, form.ShiftOccurrenceUtc,
                    form.ShiftOccurrenceEndUtc, MapForm(form), zoneId, form.ShiftDeletedUtc.HasValue);
                if (index >= 0) result[index] = occurrence;
                else result.Add(occurrence);
            }
            return result.OrderBy(r => r.StartUtc).ToList();
        }
    }

    private static void RecordActivity(QualityCycleForm form, List<QualityCycleFormItemDto>? items, List<string>? photoUrls, string? cleanerNotes, TimeProvider clock, List<QualityCyclePhoto>? photos)
    {
            if (form.ShiftDeletedUtc.HasValue) throw new QualityCycleConflictException("This shift was deleted. Its checklist is retained as history.");
            if (form.IsSubmitted) throw new QualityCycleConflictException("This shift task has already been completed.");
            if (!form.StartedAtUtc.HasValue && form.ShiftOccurrenceUtc > clock.GetUtcNow().UtcDateTime)
                throw new QualityCycleConflictException("This shift task has not started yet.");
            var domainItems = items?.Select(i => new QualityCycleFormItem
            {
                ItemText = i.ItemText,
                IsChecked = i.IsChecked
            }).ToList() ?? new List<QualityCycleFormItem>();

            try { form.Submit(domainItems, photoUrls, cleanerNotes, photos, clock.GetUtcNow().UtcDateTime); }
            catch (ArgumentException ex)
            {
                throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure(
                    ex.ParamName == "photos" ? "Photos" : "Checklist", ex.Message) });
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
            f.Photos.Count > 0 ? f.Photos.Select(p => p.Url).ToList() : f.PhotoUrls,
            f.CleanerNotes,
            f.IsSubmitted,
            f.SubmittedUtc,
            f.CreatedUtc,
            f.TaskStatus) { StartedAtUtc = f.StartedAtUtc, EndedAtUtc = f.EndedAtUtc, Photos = f.Photos };
}

public class QualityCycleValidators
{
    public class CreateQualityCycleTemplateCommandValidator : AbstractValidator<CreateQualityCycleTemplateCommand>
    {
        public CreateQualityCycleTemplateCommandValidator()
        {
            RuleFor(x => x.CompanyId).NotEqual(Guid.Empty).When(x => x.CompanyId.HasValue);
            RuleFor(x => x.BranchId).NotEqual(Guid.Empty).When(x => x.BranchId.HasValue);
            RuleFor(x => x.Title).NotEmpty().WithMessage("Template title is required.");
            RuleFor(x => x.Items).NotEmpty().WithMessage("At least one checklist item is required.");
        }
    }

    public class UpdateQualityCycleTemplateCommandValidator : AbstractValidator<UpdateQualityCycleTemplateCommand>
    {
        public UpdateQualityCycleTemplateCommandValidator()
        {
            RuleFor(x => x.CompanyId).NotEqual(Guid.Empty).When(x => x.CompanyId.HasValue);
            RuleFor(x => x.BranchId).NotEqual(Guid.Empty).When(x => x.BranchId.HasValue);
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
