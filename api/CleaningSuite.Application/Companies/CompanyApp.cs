using CleaningSuite.Application.Common;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
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

            await _repository.SaveAsync(company, ct);
            return MapCompany(company);
        }
    }

    public class UpdateCompanyCommandHandler : IRequestHandler<UpdateCompanyCommand, CompanyDto>
    {
        private readonly ICompanyRepository _repository;

        public UpdateCompanyCommandHandler(ICompanyRepository repository)
        {
            _repository = repository;
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
            return MapCompany(company);
        }
    }

    public class DeactivateCompanyCommandHandler : IRequestHandler<DeactivateCompanyCommand>
    {
        private readonly ICompanyRepository _repository;

        public DeactivateCompanyCommandHandler(ICompanyRepository repository)
        {
            _repository = repository;
        }

        public async Task Handle(DeactivateCompanyCommand request, CancellationToken ct)
        {
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
        ITenantContext tenant) : IRequestHandler<DeleteCompanyCommand>
    {
        public async Task Handle(DeleteCompanyCommand request, CancellationToken ct)
        {
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
                await _repository.SaveAsync(branch, ct);
                return MapBranch(branch);
            }
        }

        public class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand, BranchDto>
        {
            private readonly IBranchRepository _repository;

            public UpdateBranchCommandHandler(IBranchRepository repository)
            {
                _repository = repository;
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

            public DeactivateBranchCommandHandler(IBranchRepository repository)
            {
                _repository = repository;
            }

            public async Task Handle(DeactivateBranchCommand request, CancellationToken ct)
            {
                var branch = await _repository.GetAsync(request.Id, ct)
                    ?? throw new NotFoundException("Branch", request.Id);
                branch.Deactivate();
                await _repository.SaveAsync(branch, ct);
            }
        }

        public sealed class DeleteBranchCommandHandler(IBranchRepository branches, IShiftRepository shifts,
            IQualityCycleRepository forms) : IRequestHandler<DeleteBranchCommand>
        {
            public async Task Handle(DeleteBranchCommand request, CancellationToken ct)
            {
                var branch = await branches.GetAsync(request.Id, ct) ?? throw new NotFoundException("Branch", request.Id);
                if (branch.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the branch before deleting it.");
                if ((await shifts.ListForReportingAsync(ct)).Any(s => s.BranchId == branch.Id)
                    || (await forms.ListFormsAsync(null, null, null, ct, null, null, null, branch.Id)).Count > 0)
                    throw new InactiveRecordDeleteConflictException("This branch has shift or checklist history and must be retained.");
                await branches.DeleteAsync(branch.Id, ct);
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
