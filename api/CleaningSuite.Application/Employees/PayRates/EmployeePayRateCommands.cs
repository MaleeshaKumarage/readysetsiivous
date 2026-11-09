using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Employees;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Employees.PayRates;

public record EmployeePayRateDto(Guid Id, decimal HourlyRate, DateOnly EffectiveFrom, string Source, DateTime CreatedUtc);
public record EmployeeLegacyPayRateDto(decimal? Amount, bool Confirmed);
public record EmployeePayProfileDto(Guid EmployeeId, IReadOnlyList<EmployeePayRateDto> Rates, EmployeeLegacyPayRateDto Legacy);

public record GetEmployeePayProfileQuery(Guid EmployeeId) : IRequest<EmployeePayProfileDto>;

public record AddEmployeePayRateCommand(Guid EmployeeId, decimal HourlyRate, DateOnly EffectiveFrom, string RecordedBySubject)
    : IRequest<EmployeePayProfileDto>;

public record ConfirmLegacyPayRateCommand(Guid EmployeeId, DateOnly EffectiveFrom, string RecordedBySubject)
    : IRequest<EmployeePayProfileDto>;

public class GetEmployeePayProfileHandler(IEmployeeRepository employees, IEmployeePayRateRepository rates)
    : IRequestHandler<GetEmployeePayProfileQuery, EmployeePayProfileDto>
{
    public async Task<EmployeePayProfileDto> Handle(GetEmployeePayProfileQuery request, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", request.EmployeeId);
        return await PayProfile.BuildAsync(employee, rates, ct);
    }
}

public class AddEmployeePayRateHandler(IEmployeeRepository employees, IEmployeePayRateRepository rates)
    : IRequestHandler<AddEmployeePayRateCommand, EmployeePayProfileDto>
{
    public async Task<EmployeePayProfileDto> Handle(AddEmployeePayRateCommand request, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", request.EmployeeId);
        var refusal = EmployeePayRateRules.ValidateRate(request.HourlyRate);
        if (refusal is not null) throw new ValidationException(refusal);

        await rates.AddAsync(new EmployeePayRate
        {
            Id = EmployeePayRateRules.IdFor(employee.Id, request.EffectiveFrom),
            EmployeeId = employee.Id,
            HourlyRate = request.HourlyRate,
            EffectiveFrom = request.EffectiveFrom,
            Source = EmployeePayRate.SourceManual,
            RecordedBySubject = request.RecordedBySubject,
        }, ct);
        return await PayProfile.BuildAsync(employee, rates, ct);
    }
}

public class ConfirmLegacyPayRateHandler(IEmployeeRepository employees, IEmployeePayRateRepository rates)
    : IRequestHandler<ConfirmLegacyPayRateCommand, EmployeePayProfileDto>
{
    public async Task<EmployeePayProfileDto> Handle(ConfirmLegacyPayRateCommand request, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", request.EmployeeId);
        if (employee.PayRate is not decimal legacyRate)
            throw new ValidationException("This employee has no legacy pay rate to confirm.");
        var rateRefusal = EmployeePayRateRules.ValidateRate(legacyRate);
        if (rateRefusal is not null) throw new ValidationException(rateRefusal);

        var existing = await rates.ListAsync(employee.Id, ct);
        var refusal = EmployeePayRateRules.LegacyConfirmationRefusal(existing, request.EffectiveFrom);
        if (refusal is not null) throw new EmployeePayRateConflictException(refusal);

        await rates.AddAsync(new EmployeePayRate
        {
            Id = EmployeePayRateRules.IdFor(employee.Id, request.EffectiveFrom),
            EmployeeId = employee.Id,
            HourlyRate = legacyRate,
            EffectiveFrom = request.EffectiveFrom,
            Source = EmployeePayRate.SourceLegacyConfirmed,
            RecordedBySubject = request.RecordedBySubject,
        }, ct);
        return await PayProfile.BuildAsync(employee, rates, ct);
    }
}

internal static class PayProfile
{
    public static async Task<EmployeePayProfileDto> BuildAsync(Employee employee, IEmployeePayRateRepository rates, CancellationToken ct)
    {
        var entries = await rates.ListAsync(employee.Id, ct);
        var history = entries
            .OrderByDescending(entry => entry.EffectiveFrom)
            .Select(entry => new EmployeePayRateDto(entry.Id, entry.HourlyRate, entry.EffectiveFrom, entry.Source, entry.CreatedUtc))
            .ToArray();
        var legacy = new EmployeeLegacyPayRateDto(employee.PayRate, entries.Any(entry => entry.Source == EmployeePayRate.SourceLegacyConfirmed));
        return new EmployeePayProfileDto(employee.Id, history, legacy);
    }
}
