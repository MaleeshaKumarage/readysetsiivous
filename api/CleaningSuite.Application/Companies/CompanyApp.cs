using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Partners;
using Marten;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using MediatR;
using System.Text.Json.Serialization;

namespace CleaningSuite.Application.Companies;

public record CompanyDto(
    Guid Id,
    string BusinessId,
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes,
    bool IsActive,
    bool IsPartnerEnabled = false);

public record BranchDto(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Street,
    string? PostalCode,
    string? City,
    string? Country,
    string? ContactPhone,
    bool IsActive,
    string? ContactEmail = null,
    int? RoomCount = null, int? BedCount = null, decimal? PriceNet = null,
    decimal? VatRatePercent = null, int? EstimatedDurationMinutes = null,
    int PricingVersion = 0, bool PricingReady = false, DateTime? QuoteRequestedUtc = null,
    DateTime? QuoteRejectedUtc = null, string? QuoteRejectionReason = null);

public record CompanyDetailDto(CompanyDto Company, IReadOnlyList<BranchDto> Branches);

public interface ICompanyRepository
{
    Task<Company?> GetAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Company> Items, int Total)> ListAsync(string? search, int skip, int take, CancellationToken ct = default);
    Task<(IReadOnlyList<Company> Items, int Total)> ListByStatusAsync(string? search, string status, int skip, int take, CancellationToken ct = default);
    Task<CompanyCounts> CountByStatusAsync(CancellationToken ct = default);
    Task SaveAsync(Company company, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IBranchRepository
{
    Task<Branch?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Branch>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<IReadOnlyList<Branch>> ListByCompanyAsync(Guid companyId, bool includeInactive, CancellationToken ct = default);
    Task SaveAsync(Branch branch, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public record CreateCompanyCommand(
    string BusinessId,
    string Name,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? Notes = null,
    bool IsPartnerEnabled = false) : IRequest<CompanyDto>;

public record UpdateCompanyCommand(
    Guid Id,
    string BusinessId,
    string Name,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? Notes = null,
    bool IsActive = true,
    bool? IsPartnerEnabled = null) : IRequest<CompanyDto>;

public record DeactivateCompanyCommand(Guid Id) : IRequest;
public record ReactivateCompanyCommand(Guid Id) : IRequest;
public record DeleteCompanyCommand(Guid Id) : IRequest;
// Clears a company in one step: its shifts, places, partner requests and issued invoices (voided, never deleted).
// Checklist submissions are kept as separate records. Signed agreements are kept unless DeleteSignedAgreements is set.
public record ClearCompanyRelatedCommand(Guid Id, bool DeleteSignedAgreements = false) : IRequest<ClearCompanyRelatedResultDto>;
public record ClearCompanyRelatedResultDto(int ShiftsDeleted, int EmployeesUnassigned, int BranchesDeleted,
    int InvoicesVoided = 0, int PartnerRecordsDeleted = 0, int AgreementsDeleted = 0);

public record CompanyCounts(int All, int Active, int Inactive);

public record ListCompaniesQuery(string? Search, int Skip, int Take, string Status = "all") : IRequest<Paged<CompanyDto>>;

public record CompanySummaryQuery : IRequest<CompanyCounts>;

public record GetCompanyQuery(Guid Id) : IRequest<CompanyDetailDto>;

public record CreateBranchCommand(
    Guid CompanyId,
    string Name,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    int? RoomCount = null, int? BedCount = null, decimal? PriceNet = null,
    decimal? VatRatePercent = null, int? EstimatedDurationMinutes = null) : IRequest<BranchDto>;

public record UpdateBranchCommand(
    Guid Id,
    string Name,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? ContactPhone = null,
    bool IsActive = true,
    string? ContactEmail = null,
    int? RoomCount = null, int? BedCount = null, decimal? PriceNet = null,
    decimal? VatRatePercent = null, int? EstimatedDurationMinutes = null) : IRequest<BranchDto>
{
    // JSON must distinguish an old client's omitted field from an explicit null
    // clearing an optional count. Keep the positional constructor for existing callers.
    [JsonConstructor]
    public UpdateBranchCommand() : this(Guid.Empty, "") { }

    private int? _roomCount = RoomCount;
    private int? _bedCount = BedCount;
    [JsonIgnore] public bool HasRoomCount { get; private init; } = RoomCount.HasValue;
    [JsonIgnore] public bool HasBedCount { get; private init; } = BedCount.HasValue;
    public int? RoomCount { get => _roomCount; init { _roomCount = value; HasRoomCount = true; } }
    public int? BedCount { get => _bedCount; init { _bedCount = value; HasBedCount = true; } }
}

public record DeactivateBranchCommand(Guid Id) : IRequest;
public record ReactivateBranchCommand(Guid Id) : IRequest;
public record DeleteBranchCommand(Guid Id) : IRequest;

public class CompanyHandlers
{
    public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, CompanyDto>
    {
        private readonly ICompanyRepository _repository;

        public CreateCompanyCommandHandler(ICompanyRepository repository)
        {
            _repository = repository;
        }

        public async Task<CompanyDto> Handle(CreateCompanyCommand request, CancellationToken ct)
        {
            var company = Company.Create(
                request.BusinessId,
                request.Name,
                request.ContactName,
                request.ContactEmail,
                request.ContactPhone,
                request.Notes);

            company.IsPartnerEnabled = request.IsPartnerEnabled;
            await _repository.SaveAsync(company, ct);
            return MapCompany(company);
        }
    }

    public class UpdateCompanyCommandHandler : IRequestHandler<UpdateCompanyCommand, CompanyDto>
    {
        private readonly ICompanyRepository _repository;

        private readonly IShiftWorkReservation? _reservation;

            public UpdateCompanyCommandHandler(ICompanyRepository repository, IShiftWorkReservation? reservation = null)
        {
            _repository = repository; _reservation = reservation;
        }

        public async Task<CompanyDto> Handle(UpdateCompanyCommand request, CancellationToken ct)
        {
            await using var lease = _reservation is null ? null : await _reservation.AcquireAsync(ct);
            var company = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Company", request.Id);
            company.Update(
                request.BusinessId,
                request.Name,
                request.ContactName,
                request.ContactEmail,
                request.ContactPhone,
                request.Notes,
                request.IsActive);

            if (request.IsPartnerEnabled.HasValue) company.IsPartnerEnabled = request.IsPartnerEnabled.Value;
            await _repository.SaveAsync(company, ct);
            return MapCompany(company);
        }
    }

    public class DeactivateCompanyCommandHandler : IRequestHandler<DeactivateCompanyCommand>
    {
        private readonly ICompanyRepository _repository;

        private readonly IShiftWorkReservation? _reservation;

            private readonly IShiftRepository _shifts;

            public DeactivateCompanyCommandHandler(ICompanyRepository repository, IShiftWorkReservation? reservation, IShiftRepository shifts)
        {
            _repository = repository; _reservation = reservation; _shifts = shifts;
        }

        public async Task Handle(DeactivateCompanyCommand request, CancellationToken ct)
        {
            await using var lease = _reservation is null ? null : await _reservation.AcquireAsync(ct);
            var company = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Company", request.Id);
            // Shifts keep running for cleaners after the company flag changes, so the company must be emptied of active shifts first.
            var active = (await _shifts.ListAsync(request.Id, null, ct)).Count(shift => shift.IsActive);
            if (active > 0)
                throw new CleaningSuite.Application.Shifts.ShiftConflictException(
                    $"This company still has {active} active shift(s). Deactivate or move them first, then deactivate the company.");
            company.Deactivate();
            await _repository.SaveAsync(company, ct);
        }
    }

    public class ReactivateCompanyCommandHandler(ICompanyRepository repository) : IRequestHandler<ReactivateCompanyCommand>
    {
        public async Task Handle(ReactivateCompanyCommand request, CancellationToken ct)
        {
            var company = await repository.GetAsync(request.Id, ct) ?? throw new NotFoundException("Company", request.Id);
            company.IsActive = true;
            company.UpdatedUtc = DateTime.UtcNow;
            await repository.SaveAsync(company, ct);
        }
    }

    public sealed class DeleteCompanyCommandHandler(ICompanyRepository companies, IBranchRepository branches,
        IShiftRepository shifts, IAgreementRepository agreements, IQualityCycleRepository forms,
        ITenantContext tenant, IPartnerStore? partnerStore = null, IShiftWorkReservation? reservation = null) : IRequestHandler<DeleteCompanyCommand>
    {
        public async Task Handle(DeleteCompanyCommand request, CancellationToken ct)
        {
            await using var lease = reservation is null ? null : await reservation.AcquireAsync(ct);
            if (partnerStore is not null && (await partnerStore.Session.Query<PartnerCleaningRequest>().AnyAsync(x => x.CompanyId == request.Id, ct)
                || await partnerStore.Session.Query<PartnerChangeRequest>().AnyAsync(x => x.CompanyId == request.Id, ct)
                || await partnerStore.Session.Query<CleaningSuite.Domain.Employees.Employee>().AnyAsync(x => x.PartnerCompanyId == request.Id, ct)))
                throw new InactiveRecordDeleteConflictException("This company has partner request history and must be retained.");
            var company = await companies.GetAsync(request.Id, ct) ?? throw new NotFoundException("Company", request.Id);
            if (company.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the company before deleting it.");
            if ((await branches.ListByCompanyAsync(company.Id, true, ct)).Count > 0)
                throw new InactiveRecordDeleteConflictException("Delete or move the company's branches before deleting it.");
            if ((await shifts.ListForReportingAsync(ct)).Any(s => s.CompanyId == company.Id)
                || (await forms.ListFormsAsync(null, null, null, ct, null, null, company.Id, null)).Count > 0)
                throw new InactiveRecordDeleteConflictException("This company has shift or checklist history and must be retained.");
            if ((await agreements.ListAsync(tenant.TenantId, ct)).Any(a => a.CompanyId == company.Id))
                throw new InactiveRecordDeleteConflictException("This company has agreement records and must be retained.");
            await companies.DeleteAsync(company.Id, ct);
        }
    }

    // One-step clear: removes the company's shifts, places, partner requests and agreements (when asked), and voids its
    // open invoices. Paid invoices and partner user accounts are refused up front, so a refusal changes nothing.
    public sealed class ClearCompanyRelatedCommandHandler(ICompanyRepository companies, IBranchRepository branches,
        IShiftRepository shifts, IQualityCycleRepository forms, IAgreementRepository agreements, ITenantContext tenant,
        IPartnerStore? partnerStore = null, IShiftWorkReservation? reservation = null, IInvoiceRepository? invoices = null)
        : IRequestHandler<ClearCompanyRelatedCommand, ClearCompanyRelatedResultDto>
    {
        public async Task<ClearCompanyRelatedResultDto> Handle(ClearCompanyRelatedCommand request, CancellationToken ct)
        {
            await using var lease = reservation is null ? null : await reservation.AcquireAsync(ct);
            var company = await companies.GetAsync(request.Id, ct) ?? throw new NotFoundException("Company", request.Id);

            var companyShifts = await shifts.ListAsync(company.Id, null, ct);
            var companyBranches = await branches.ListByCompanyAsync(company.Id, true, ct);
            var branchIds = companyBranches.Select(b => b.Id).ToArray();
            var openInvoices = invoices is null ? [] : (await invoices.ListAsync(null, null, ct))
                .Where(i => i.CompanyId == company.Id && i.Status != Invoice.StatusVoided).ToArray();
            var agreementsToDelete = request.DeleteSignedAgreements
                ? (await agreements.ListAsync(tenant.TenantId, ct)).Where(a => a.CompanyId == company.Id).ToArray()
                : [];

            // Refusals are decided before anything is removed.
            if (openInvoices.Any(i => i.Status == Invoice.StatusPaid))
                throw new InactiveRecordDeleteConflictException("This company has paid invoices. Paid invoices are kept as the record of what was paid; issue a credit note for any correction, then clear again.");
            if (partnerStore is not null && await partnerStore.Session.Query<CleaningSuite.Domain.Employees.Employee>().AnyAsync(x => x.PartnerCompanyId == company.Id, ct))
                throw new InactiveRecordDeleteConflictException("Partner user accounts belong to this company. Remove those accounts first, then clear again.");

            var partnerRequests = new List<PartnerCleaningRequest>();
            var partnerChanges = new List<PartnerChangeRequest>();
            var partnerNotes = new List<PartnerNotification>();
            if (partnerStore is not null)
            {
                var session = partnerStore.Session;
                partnerRequests = (await session.Query<PartnerCleaningRequest>().Where(x => x.CompanyId == company.Id || branchIds.Contains(x.BranchId)).ToListAsync(ct)).ToList();
                partnerChanges = (await session.Query<PartnerChangeRequest>().Where(x => x.CompanyId == company.Id || branchIds.Contains(x.BranchId)).ToListAsync(ct)).ToList();
                partnerNotes = (await session.Query<PartnerNotification>().Where(x => branchIds.Contains(x.BranchId)).ToListAsync(ct)).ToList();
            }
            var partnerRecordCount = partnerRequests.Count + partnerChanges.Count + partnerNotes.Count;

            // Each save commits on its own, so the clear cannot be one transaction. It stops, reports how far it got, and can
            // be run again: finished work is skipped on the next run (shifts, places and invoices that are already gone or voided).
            var now = DateTime.UtcNow;
            var removedShifts = 0; var removedPlaces = 0; var unassigned = 0; var voided = 0; var partnerRemoved = 0; var agreementsRemoved = 0;
            try
            {
                if (partnerStore is not null && partnerRecordCount > 0)
                {
                    foreach (var partnerRequest in partnerRequests) partnerStore.Session.Delete(partnerRequest);
                    foreach (var change in partnerChanges) partnerStore.Session.Delete(change);
                    foreach (var note in partnerNotes) partnerStore.Session.Delete(note);
                    await partnerStore.Session.SaveChangesAsync(ct);
                    partnerRemoved = partnerRecordCount;
                }

                foreach (var shift in companyShifts)
                {
                    if (shift.IsActive)
                    {
                        shift.EnsureQualityCycleHistory();
                        shift.Deactivate();
                        shift.RecordQualityCyclePlan(now);
                        await shifts.SaveAsync(shift, ct);
                    }
                    foreach (var assignment in await shifts.ListAssignmentsByShiftAsync(shift.Id, ct))
                    {
                        if (!assignment.IsActive) continue;
                        assignment.EndFrom(now, now);
                        await shifts.SaveAssignmentAsync(assignment, ct);
                        unassigned++;
                    }
                    await shifts.DeleteAsync(shift.Id, ct);
                    removedShifts++;
                }

                foreach (var invoice in openInvoices)
                {
                    invoice.Status = Invoice.StatusVoided;
                    await invoices!.SaveAsync(invoice, ct);
                    voided++;
                }

                foreach (var agreement in agreementsToDelete)
                {
                    await agreements.DeleteAsync(tenant.TenantId, agreement.Id, ct);
                    agreementsRemoved++;
                }

                foreach (var branch in companyBranches)
                {
                    if (branch.IsActive)
                    {
                        branch.Deactivate();
                        await branches.SaveAsync(branch, ct);
                    }
                    await branches.DeleteAsync(branch.Id, ct);
                    removedPlaces++;
                }
                return new ClearCompanyRelatedResultDto(removedShifts, unassigned, removedPlaces, voided, partnerRemoved, agreementsRemoved);
            }
            catch (Exception ex) when (ex is InactiveRecordDeleteConflictException or ShiftConflictException or OperationCanceledException)
            {
                // Specific refusals keep their meaning and message; only unexpected failures get the progress report.
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Clearing stopped after removing {removedShifts} of {companyShifts.Count} shift(s), {removedPlaces} of {companyBranches.Count} place(s) and voiding {voided} invoice(s). The company was not deactivated or deleted. Run Clear again to finish. ({ex.Message})", ex);
            }
        }
    }

    public class ListCompaniesQueryHandler : IRequestHandler<ListCompaniesQuery, Paged<CompanyDto>>
    {
        private readonly ICompanyRepository _repository;

        public ListCompaniesQueryHandler(ICompanyRepository repository)
        {
            _repository = repository;
        }

        public async Task<Paged<CompanyDto>> Handle(ListCompaniesQuery request, CancellationToken ct)
        {
            var (items, total) = await _repository.ListByStatusAsync(request.Search, request.Status, request.Skip, request.Take, ct);
            return new Paged<CompanyDto>(items.Select(MapCompany).ToList(), total);
        }
    }

    public class CompanySummaryQueryHandler : IRequestHandler<CompanySummaryQuery, CompanyCounts>
    {
        private readonly ICompanyRepository _repository;

        public CompanySummaryQueryHandler(ICompanyRepository repository)
        {
            _repository = repository;
        }

        public Task<CompanyCounts> Handle(CompanySummaryQuery request, CancellationToken ct) =>
            _repository.CountByStatusAsync(ct);
    }

    public class GetCompanyQueryHandler : IRequestHandler<GetCompanyQuery, CompanyDetailDto>
    {
        private readonly ICompanyRepository _companyRepository;
        private readonly IBranchRepository _branchRepository;

        public GetCompanyQueryHandler(ICompanyRepository companyRepository, IBranchRepository branchRepository)
        {
            _companyRepository = companyRepository;
            _branchRepository = branchRepository;
        }

        public async Task<CompanyDetailDto> Handle(GetCompanyQuery request, CancellationToken ct)
        {
            var company = await _companyRepository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Company", request.Id);
            var branches = await _branchRepository.ListByCompanyAsync(request.Id, includeInactive: true, ct);
            return new CompanyDetailDto(
                MapCompany(company),
                branches.Select(MapBranch).ToList());
        }
    }

    public class BranchCommands
    {
        public class CreateBranchCommandHandler : IRequestHandler<CreateBranchCommand, BranchDto>
        {
            private readonly IBranchRepository _repository;
            private readonly ICompanyRepository _companies;

            public CreateBranchCommandHandler(IBranchRepository repository, ICompanyRepository companies)
            {
                _repository = repository;
                _companies = companies;
            }

            public async Task<BranchDto> Handle(CreateBranchCommand request, CancellationToken ct)
            {
                _ = await _companies.GetAsync(request.CompanyId, ct)
                    ?? throw new NotFoundException("Company", request.CompanyId);
                var branch = Branch.Create(
                    request.CompanyId,
                    request.Name,
                    request.Street,
                    request.PostalCode,
                    request.City,
                    request.Country,
                    request.ContactPhone);
                branch.ContactEmail = request.ContactEmail?.Trim();
                ApplyProperty(branch, request.RoomCount, request.BedCount, request.PriceNet, request.VatRatePercent, request.EstimatedDurationMinutes);
                await _repository.SaveAsync(branch, ct);
                return MapBranch(branch);
            }
        }

        public class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand, BranchDto>
        {
            private readonly IBranchRepository _repository;

            private readonly IShiftWorkReservation? _reservation;

            public UpdateBranchCommandHandler(IBranchRepository repository, IShiftWorkReservation? reservation = null)
            {
                _repository = repository; _reservation = reservation;
            }

            public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken ct)
            {
            await using var lease = _reservation is null ? null : await _reservation.AcquireAsync(ct);
                var branch = await _repository.GetAsync(request.Id, ct)
                    ?? throw new NotFoundException("Branch", request.Id);
                branch.Update(
                    request.Name,
                    request.Street,
                    request.PostalCode,
                    request.City,
                    request.Country,
                    request.ContactPhone,
                    request.IsActive);
                branch.ContactEmail = request.ContactEmail?.Trim();
                ApplyProperty(branch,
                    request.HasRoomCount ? request.RoomCount : branch.RoomCount,
                    request.HasBedCount ? request.BedCount : branch.BedCount,
                    request.PriceNet, request.VatRatePercent, request.EstimatedDurationMinutes);
                await _repository.SaveAsync(branch, ct);
                return MapBranch(branch);
            }
        }

        public class ReactivateBranchCommandHandler(IBranchRepository repository, ICompanyRepository companies) : IRequestHandler<ReactivateBranchCommand>
        {
            public async Task Handle(ReactivateBranchCommand request, CancellationToken ct)
            {
                var branch = await repository.GetAsync(request.Id, ct) ?? throw new NotFoundException("Branch", request.Id);
                _ = await companies.GetAsync(branch.CompanyId, ct) ?? throw new NotFoundException("Company", branch.CompanyId);
                branch.IsActive = true;
                branch.UpdatedUtc = DateTime.UtcNow;
                await repository.SaveAsync(branch, ct);
            }
        }

        public class DeactivateBranchCommandHandler : IRequestHandler<DeactivateBranchCommand>
        {
            private readonly IBranchRepository _repository;

            private readonly IShiftWorkReservation? _reservation;

            public DeactivateBranchCommandHandler(IBranchRepository repository, IShiftWorkReservation? reservation = null)
            {
                _repository = repository; _reservation = reservation;
            }

            public async Task Handle(DeactivateBranchCommand request, CancellationToken ct)
            {
                await using var lease = _reservation is null ? null : await _reservation.AcquireAsync(ct);
                var branch = await _repository.GetAsync(request.Id, ct)
                    ?? throw new NotFoundException("Branch", request.Id);
                branch.Deactivate();
                await _repository.SaveAsync(branch, ct);
            }
        }

        public sealed class DeleteBranchCommandHandler(IBranchRepository branches, IShiftRepository shifts,
            IQualityCycleRepository forms, IPartnerStore? partnerStore = null, IShiftWorkReservation? reservation = null) : IRequestHandler<DeleteBranchCommand>
        {
            public async Task Handle(DeleteBranchCommand request, CancellationToken ct)
            {
                await using var lease = reservation is null ? null : await reservation.AcquireAsync(ct);
                if (partnerStore is not null && (await partnerStore.Session.Query<PartnerCleaningRequest>().AnyAsync(x => x.BranchId == request.Id, ct)
                    || await partnerStore.Session.Query<PartnerChangeRequest>().AnyAsync(x => x.BranchId == request.Id, ct)
                    || await partnerStore.Session.Query<PartnerNotification>().AnyAsync(x => x.BranchId == request.Id, ct)))
                    throw new InactiveRecordDeleteConflictException("This property has partner request history and must be retained.");
                var branch = await branches.GetAsync(request.Id, ct) ?? throw new NotFoundException("Branch", request.Id);
                if (branch.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the branch before deleting it.");
                if ((await shifts.ListForReportingAsync(ct)).Any(s => s.BranchId == branch.Id)
                    || (await forms.ListFormsAsync(null, null, null, ct, null, null, null, branch.Id)).Count > 0)
                    throw new InactiveRecordDeleteConflictException("This branch has shift or checklist history and must be retained.");
                await branches.DeleteAsync(branch.Id, ct);
            }
        }
    }

    public static CompanyDto MapCompany(Company company) =>
        new(
            company.Id,
            company.BusinessId,
            company.Name,
            company.ContactName,
            company.ContactEmail,
            company.ContactPhone,
            company.Notes,
            company.IsActive, company.IsPartnerEnabled);

    public static BranchDto MapBranch(Branch branch) =>
        new(
            branch.Id,
            branch.CompanyId,
            branch.Name,
            branch.Street,
            branch.PostalCode,
            branch.City,
            branch.Country,
            branch.ContactPhone,
            branch.IsActive,
            branch.ContactEmail, branch.RoomCount, branch.BedCount, branch.PriceNet, branch.VatRatePercent,
            branch.EstimatedDurationMinutes, branch.PricingVersion, branch.PricingReady, branch.QuoteRequestedUtc,
            branch.QuoteRejectedUtc, branch.QuoteRejectionReason);
    private static void ApplyProperty(Branch branch, int? rooms, int? beds, decimal? price, decimal? vat, int? duration)
    {
        try
        {
            branch.SetPropertyCounts(rooms, beds);
            if (price.HasValue || vat.HasValue || duration.HasValue)
            {
                if (!price.HasValue || !vat.HasValue || !duration.HasValue) throw new ValidationException([new FluentValidation.Results.ValidationFailure("Pricing", "Net price, VAT and estimated duration must be supplied together.")]);
                branch.SetPricing(price.Value, vat.Value, duration.Value);
            }
        }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
    }
}

public class CompanyValidators
{
    // Finnish business IDs: seven digits, a dash and one check digit, such as 1234567-8.
    public const string BusinessIdPattern = @"^\d{7}-\d$";
    public const string BusinessIdMessage = "Business ID must look like 1234567-8: seven digits, a dash and one check digit. Check the company's registration, then try again.";

    public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
    {
        public CreateCompanyCommandValidator()
        {
            RuleFor(x => x.BusinessId).NotEmpty().WithMessage("Business ID is mandatory.");
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is mandatory.");
            RuleFor(x => x.BusinessId).Matches(BusinessIdPattern).WithMessage(BusinessIdMessage).When(x => !string.IsNullOrWhiteSpace(x.BusinessId));
            RuleFor(x => x.Name).MaximumLength(200).WithMessage("Company name can be at most 200 characters. Shorten it, then save again.");
            RuleFor(x => x.ContactEmail).EmailAddress().WithMessage("Enter a valid contact email, such as name@company.fi, or leave the field empty.").When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        }
    }

    public class UpdateCompanyCommandValidator : AbstractValidator<UpdateCompanyCommand>
    {
        public UpdateCompanyCommandValidator()
        {
            RuleFor(x => x.Id).NotEqual(Guid.Empty);
            RuleFor(x => x.BusinessId).NotEmpty().WithMessage("Business ID is mandatory.");
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is mandatory.");
        }
    }

    public class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
    {
        public CreateBranchCommandValidator()
        {
            RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
            RuleFor(x => x.CompanyId).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().WithMessage("Branch name is mandatory.");
        }
    }

    public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
    {
        public UpdateBranchCommandValidator()
        {
            RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
            RuleFor(x => x.Id).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().WithMessage("Branch name is mandatory.");
        }
    }
}
