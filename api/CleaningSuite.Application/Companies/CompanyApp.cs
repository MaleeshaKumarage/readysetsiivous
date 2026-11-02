using CleaningSuite.Application.Employees;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Partners;
using Marten;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
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
    decimal? SizeSquareMeters = null, Guid? PreferredCleanerEmployeeId = null);

public record CompanyDetailDto(CompanyDto Company, IReadOnlyList<BranchDto> Branches);

public interface ICompanyRepository
{
    Task<Company?> GetAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Company> Items, int Total)> ListAsync(string? search, int skip, int take, CancellationToken ct = default);
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

public record ListCompaniesQuery(string? Search, int Skip, int Take) : IRequest<Paged<CompanyDto>>;

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
    decimal? VatRatePercent = null, int? EstimatedDurationMinutes = null, decimal? SizeSquareMeters = null,
    Guid? PreferredCleanerEmployeeId = null) : IRequest<BranchDto>;

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
    decimal? VatRatePercent = null, int? EstimatedDurationMinutes = null, decimal? SizeSquareMeters = null,
    Guid? PreferredCleanerEmployeeId = null) : IRequest<BranchDto>
{
    // JSON must distinguish an old client's omitted field from an explicit null
    // clearing an optional count. Keep the positional constructor for existing callers.
    [JsonConstructor]
    public UpdateBranchCommand() : this(Guid.Empty, "") { }

    private decimal? _sizeSquareMeters = SizeSquareMeters;
    private Guid? _preferredCleanerEmployeeId = PreferredCleanerEmployeeId;
    [JsonIgnore] public bool HasSizeSquareMeters { get; private init; } = SizeSquareMeters.HasValue;
    [JsonIgnore] public bool HasPreferredCleanerEmployeeId { get; private init; } = PreferredCleanerEmployeeId.HasValue;
    public decimal? SizeSquareMeters { get => _sizeSquareMeters; init { _sizeSquareMeters = value; HasSizeSquareMeters = true; } }
    public Guid? PreferredCleanerEmployeeId { get => _preferredCleanerEmployeeId; init { _preferredCleanerEmployeeId = value; HasPreferredCleanerEmployeeId = true; } }
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

            public DeactivateCompanyCommandHandler(ICompanyRepository repository, IShiftWorkReservation? reservation = null)
        {
            _repository = repository; _reservation = reservation;
        }

        public async Task Handle(DeactivateCompanyCommand request, CancellationToken ct)
        {
            await using var lease = _reservation is null ? null : await _reservation.AcquireAsync(ct);
            var company = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Company", request.Id);
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

    public class ListCompaniesQueryHandler : IRequestHandler<ListCompaniesQuery, Paged<CompanyDto>>
    {
        private readonly ICompanyRepository _repository;

        public ListCompaniesQueryHandler(ICompanyRepository repository)
        {
            _repository = repository;
        }

        public async Task<Paged<CompanyDto>> Handle(ListCompaniesQuery request, CancellationToken ct)
        {
            var (items, total) = await _repository.ListAsync(request.Search, request.Skip, request.Take, ct);
            return new Paged<CompanyDto>(items.Select(MapCompany).ToList(), total);
        }
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
            private readonly IEmployeeRepository? _employees;

            public CreateBranchCommandHandler(IBranchRepository repository, ICompanyRepository companies, IEmployeeRepository? employees = null)
            {
                _repository = repository;
                _companies = companies; _employees = employees;
            }

            public async Task<BranchDto> Handle(CreateBranchCommand request, CancellationToken ct)
            {
                var company = await _companies.GetAsync(request.CompanyId, ct)
                    ?? throw new NotFoundException("Company", request.CompanyId);
                var name = BranchName(company.IsPartnerEnabled, request.Name, request.Street, request.PostalCode, request.City);
                var branch = Branch.Create(
                    request.CompanyId,
                    name,
                    request.Street,
                    request.PostalCode,
                    request.City,
                    request.Country,
                    request.ContactPhone);
                await ApplyBranchSettings(branch, request.SizeSquareMeters, request.PreferredCleanerEmployeeId, _employees, ct);
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

            private readonly IEmployeeRepository? _employees;
            private readonly ICompanyRepository? _companies;
            public UpdateBranchCommandHandler(IBranchRepository repository, IShiftWorkReservation? reservation = null, IEmployeeRepository? employees = null, ICompanyRepository? companies = null)
            {
                _repository = repository; _reservation = reservation; _employees = employees; _companies = companies;
            }

            public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken ct)
            {
            await using var lease = _reservation is null ? null : await _reservation.AcquireAsync(ct);
                var branch = await _repository.GetAsync(request.Id, ct)
                    ?? throw new NotFoundException("Branch", request.Id);
                var company = _companies is null ? null : await _companies.GetAsync(branch.CompanyId, ct);
                var name = BranchName(company?.IsPartnerEnabled == true, request.Name, request.Street, request.PostalCode, request.City);
                branch.Update(
                    name,
                    request.Street,
                    request.PostalCode,
                    request.City,
                    request.Country,
                    request.ContactPhone,
                    request.IsActive);
                await ApplyBranchSettings(branch,
                    request.HasSizeSquareMeters ? request.SizeSquareMeters : branch.SizeSquareMeters,
                    request.HasPreferredCleanerEmployeeId ? request.PreferredCleanerEmployeeId : branch.PreferredCleanerEmployeeId,
                    _employees, ct, request.HasPreferredCleanerEmployeeId && request.PreferredCleanerEmployeeId != branch.PreferredCleanerEmployeeId);
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
            branch.EstimatedDurationMinutes, branch.PricingVersion, branch.PricingReady, branch.QuoteRequestedUtc, branch.SizeSquareMeters, branch.PreferredCleanerEmployeeId);
    private static string BranchName(bool partner, string name, string? street, string? postalCode, string? city)
    {
        try
        {
            if (!partner && string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Branch name is mandatory.");
            return partner ? Branch.PartnerPropertyName(street, postalCode, city) : name;
        }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
    }

    private static async Task ApplyBranchSettings(Branch branch, decimal? size, Guid? preferred, IEmployeeRepository? employees, CancellationToken ct, bool validatePreferred = true)
    {
        if (validatePreferred && preferred.HasValue)
        {
            var employee = employees is null ? null : await employees.GetByIdAsync(preferred.Value, ct);
            if (employee is not { IsActive: true, IdentitySyncPending: false } || !Employee.IsWorkRole(employee.Role)
                || string.IsNullOrWhiteSpace(employee.KeycloakUserId))
                throw new ValidationException("Select an active, synchronized cleaner with a linked login.");
        }
        try { branch.SetSize(size); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
        branch.PreferredCleanerEmployeeId = preferred;
    }

    private static void ApplyProperty(Branch branch, int? rooms, int? beds, decimal? price, decimal? vat, int? duration)
    {
        try
        {
            branch.SetPropertyCounts(rooms, beds);
            if (price.HasValue || vat.HasValue || duration.HasValue)
            {
                if (!price.HasValue || !vat.HasValue || !duration.HasValue) throw new ValidationException("Net price, VAT and estimated duration must be supplied together.");
                branch.SetPricing(price.Value, vat.Value, duration.Value);
            }
        }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }
    }
}

public class CompanyValidators
{
    public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
    {
        public CreateCompanyCommandValidator()
        {
            RuleFor(x => x.BusinessId).NotEmpty().WithMessage("Business ID is mandatory.");
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is mandatory.");
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

        }
    }

    public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
    {
        public UpdateBranchCommandValidator()
        {
            RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
            RuleFor(x => x.Id).NotEqual(Guid.Empty);

        }
    }
}
