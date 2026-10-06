using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Companies;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Companies;

public record CompanyDto(
    Guid Id,
    string BusinessId,
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes,
    bool IsActive);

public record BranchDto(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Street,
    string? PostalCode,
    string? City,
    string? Country,
    string? ContactPhone,
    bool IsActive);

public record CompanyDetailDto(CompanyDto Company, IReadOnlyList<BranchDto> Branches);

public interface ICompanyRepository
{
    Task<Company?> GetAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Company> Items, int Total)> ListAsync(string? search, int skip, int take, CancellationToken ct = default);
    Task SaveAsync(Company company, CancellationToken ct = default);
}

public interface IBranchRepository
{
    Task<Branch?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Branch>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<IReadOnlyList<Branch>> ListByCompanyAsync(Guid companyId, bool includeInactive, CancellationToken ct = default);
    Task SaveAsync(Branch branch, CancellationToken ct = default);
}

public record CreateCompanyCommand(
    string BusinessId,
    string Name,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? Notes = null) : IRequest<CompanyDto>;

public record UpdateCompanyCommand(
    Guid Id,
    string BusinessId,
    string Name,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? Notes = null,
    bool IsActive = true) : IRequest<CompanyDto>;

public record DeactivateCompanyCommand(Guid Id) : IRequest;

public record ListCompaniesQuery(string? Search, int Skip, int Take) : IRequest<Paged<CompanyDto>>;

public record GetCompanyQuery(Guid Id) : IRequest<CompanyDetailDto>;

public record ListBranchesQuery(Guid CompanyId) : IRequest<IReadOnlyList<BranchDto>>;

public record CreateBranchCommand(
    Guid CompanyId,
    string Name,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? ContactPhone = null) : IRequest<BranchDto>;

public record UpdateBranchCommand(
    Guid Id,
    string Name,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? ContactPhone = null,
    bool IsActive = true) : IRequest<BranchDto>;

public record DeactivateBranchCommand(Guid Id) : IRequest;

public class CompanyHandlers
{
    public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, CompanyDto>
    {
        private readonly ICompanyRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public CreateCompanyCommandHandler(ICompanyRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
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

            await _repository.SaveAsync(company, ct);
            _cacheService?.RemoveByPrefix("companies");
            return MapCompany(company);
        }
    }

    public class UpdateCompanyCommandHandler : IRequestHandler<UpdateCompanyCommand, CompanyDto>
    {
        private readonly ICompanyRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public UpdateCompanyCommandHandler(ICompanyRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task<CompanyDto> Handle(UpdateCompanyCommand request, CancellationToken ct)
        {
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

            await _repository.SaveAsync(company, ct);
            _cacheService?.RemoveByPrefix("companies");
            return MapCompany(company);
        }
    }

    public class DeactivateCompanyCommandHandler : IRequestHandler<DeactivateCompanyCommand>
    {
        private readonly ICompanyRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public DeactivateCompanyCommandHandler(ICompanyRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task Handle(DeactivateCompanyCommand request, CancellationToken ct)
        {
            var company = await _repository.GetAsync(request.Id, ct)
                ?? throw new NotFoundException("Company", request.Id);
            company.Deactivate();
            await _repository.SaveAsync(company, ct);
            _cacheService?.RemoveByPrefix("companies");
        }
    }

    public class ListCompaniesQueryHandler : IRequestHandler<ListCompaniesQuery, Paged<CompanyDto>>
    {
        private readonly ICompanyRepository _repository;
        private readonly ITenantCacheService? _cacheService;

        public ListCompaniesQueryHandler(ICompanyRepository repository, ITenantCacheService? cacheService = null)
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task<Paged<CompanyDto>> Handle(ListCompaniesQuery request, CancellationToken ct)
        {
            var fetch = async (CancellationToken cToken) =>
            {
                var (items, total) = await _repository.ListAsync(request.Search, request.Skip, request.Take, cToken);
                return new Paged<CompanyDto>(items.Select(MapCompany).ToList(), total);
            };

            if (_cacheService is null) return await fetch(ct);

            return (await _cacheService.GetOrAddAsync("companies", $"list_{request.Search}_{request.Skip}_{request.Take}", fetch, ct: ct))!;
        }
    }

    public class GetCompanyQueryHandler : IRequestHandler<GetCompanyQuery, CompanyDetailDto>
    {
        private readonly ICompanyRepository _companyRepository;
        private readonly IBranchRepository _branchRepository;
        private readonly ITenantCacheService? _cacheService;

        public GetCompanyQueryHandler(ICompanyRepository companyRepository, IBranchRepository branchRepository, ITenantCacheService? cacheService = null)
        {
            _companyRepository = companyRepository;
            _branchRepository = branchRepository;
            _cacheService = cacheService;
        }

        public async Task<CompanyDetailDto> Handle(GetCompanyQuery request, CancellationToken ct)
        {
            var fetch = async (CancellationToken cToken) =>
            {
                var company = await _companyRepository.GetAsync(request.Id, cToken)
                    ?? throw new NotFoundException("Company", request.Id);
                var branches = await _branchRepository.ListByCompanyAsync(request.Id, includeInactive: true, cToken);
                return new CompanyDetailDto(
                    MapCompany(company),
                    branches.Select(MapBranch).ToList());
            };

            if (_cacheService is null) return await fetch(ct);

            return (await _cacheService.GetOrAddAsync("companies", $"detail_{request.Id}", fetch, ct: ct))!;
        }
    }

    public class ListBranchesQueryHandler : IRequestHandler<ListBranchesQuery, IReadOnlyList<BranchDto>>
    {
        private readonly IBranchRepository _branchRepository;
        private readonly ITenantCacheService? _cacheService;

        public ListBranchesQueryHandler(IBranchRepository branchRepository, ITenantCacheService? cacheService = null)
        {
            _branchRepository = branchRepository;
            _cacheService = cacheService;
        }

        public async Task<IReadOnlyList<BranchDto>> Handle(ListBranchesQuery request, CancellationToken ct)
        {
            var fetch = async (CancellationToken cToken) =>
            {
                var branches = await _branchRepository.ListByCompanyAsync(request.CompanyId, includeInactive: true, cToken);
                return (IReadOnlyList<BranchDto>)branches.Select(MapBranch).ToList();
            };

            if (_cacheService is null) return await fetch(ct);

            return (await _cacheService.GetOrAddAsync("companies", $"branches_{request.CompanyId}", fetch, ct: ct))!;
        }
    }

    public class BranchCommands
    {
        public class CreateBranchCommandHandler : IRequestHandler<CreateBranchCommand, BranchDto>
        {
            private readonly IBranchRepository _repository;
            private readonly ITenantCacheService? _cacheService;

            public CreateBranchCommandHandler(IBranchRepository repository, ITenantCacheService? cacheService = null)
            {
                _repository = repository;
                _cacheService = cacheService;
            }

            public async Task<BranchDto> Handle(CreateBranchCommand request, CancellationToken ct)
            {
                var branch = Branch.Create(
                    request.CompanyId,
                    request.Name,
                    request.Street,
                    request.PostalCode,
                    request.City,
                    request.Country,
                    request.ContactPhone);
                await _repository.SaveAsync(branch, ct);
                _cacheService?.RemoveByPrefix("companies");
                return MapBranch(branch);
            }
        }

        public class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand, BranchDto>
        {
            private readonly IBranchRepository _repository;
            private readonly ITenantCacheService? _cacheService;

            public UpdateBranchCommandHandler(IBranchRepository repository, ITenantCacheService? cacheService = null)
            {
                _repository = repository;
                _cacheService = cacheService;
            }

            public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken ct)
            {
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
                await _repository.SaveAsync(branch, ct);
                _cacheService?.RemoveByPrefix("companies");
                return MapBranch(branch);
            }
        }

        public class DeactivateBranchCommandHandler : IRequestHandler<DeactivateBranchCommand>
        {
            private readonly IBranchRepository _repository;
            private readonly ITenantCacheService? _cacheService;

            public DeactivateBranchCommandHandler(IBranchRepository repository, ITenantCacheService? cacheService = null)
            {
                _repository = repository;
                _cacheService = cacheService;
            }

            public async Task Handle(DeactivateBranchCommand request, CancellationToken ct)
            {
                var branch = await _repository.GetAsync(request.Id, ct)
                    ?? throw new NotFoundException("Branch", request.Id);
                branch.Deactivate();
                await _repository.SaveAsync(branch, ct);
                _cacheService?.RemoveByPrefix("companies");
            }
        }
    }

    private static CompanyDto MapCompany(Company company) =>
        new(
            company.Id,
            company.BusinessId,
            company.Name,
            company.ContactName,
            company.ContactEmail,
            company.ContactPhone,
            company.Notes,
            company.IsActive);

    private static BranchDto MapBranch(Branch branch) =>
        new(
            branch.Id,
            branch.CompanyId,
            branch.Name,
            branch.Street,
            branch.PostalCode,
            branch.City,
            branch.Country,
            branch.ContactPhone,
            branch.IsActive);
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
            RuleFor(x => x.CompanyId).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().WithMessage("Branch name is mandatory.");
        }
    }

    public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
    {
        public UpdateBranchCommandValidator()
        {
            RuleFor(x => x.Id).NotEqual(Guid.Empty);
            RuleFor(x => x.Name).NotEmpty().WithMessage("Branch name is mandatory.");
        }
    }
}
