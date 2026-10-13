using CleaningSuite.Application.Common;
using CleaningSuite.Application.Shifts;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.QualityCycle;

public record SetShiftQualityCycleTemplateCommand(Guid ShiftId, Guid? TemplateId) : IRequest<ShiftDto>;
public class SetShiftQualityCycleTemplateValidator : AbstractValidator<SetShiftQualityCycleTemplateCommand>
{
    public SetShiftQualityCycleTemplateValidator()
    {
        RuleFor(x => x.ShiftId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEqual(Guid.Empty).When(x => x.TemplateId.HasValue);
    }
}

public class SetShiftQualityCycleTemplateHandler(IShiftRepository shifts, IQualityCycleRepository templates,
    IQualityCycleDispatcher dispatcher, TimeProvider clock) : IRequestHandler<SetShiftQualityCycleTemplateCommand, ShiftDto>
{
    public async Task<ShiftDto> Handle(SetShiftQualityCycleTemplateCommand request, CancellationToken ct)
    {
        var shift = await shifts.GetAsync(request.ShiftId, ct) ?? throw new NotFoundException("Shift", request.ShiftId);
        if (!shift.IsActive) throw new ShiftConflictException("The shift is inactive.");
        if (request.TemplateId.HasValue)
        {
            var template = await templates.GetTemplateAsync(request.TemplateId.Value, ct)
                ?? throw new NotFoundException("QualityCycleTemplate", request.TemplateId.Value);
            if (!template.IsActive || (template.CompanyId.HasValue && template.CompanyId != shift.CompanyId)
                || (template.BranchId.HasValue && template.BranchId != shift.BranchId))
                throw new ShiftConflictException("Choose an active template belonging to this company/branch.");
        }
        if (shift.QualityCycleTemplateId != request.TemplateId)
        {
            shift.EnsureQualityCycleHistory();
            shift.QualityCycleTemplateId = request.TemplateId;
            shift.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            shift.RecordQualityCyclePlan(shift.UpdatedUtc);
            await shifts.SaveAsync(shift, ct);
        }
        await dispatcher.EnsureUpcomingAsync(shift.Id, ct);
        return new ShiftDto(shift.Id, shift.CompanyId, shift.BranchId, shift.Name, shift.Schedule,
            shift.Notes, shift.IsActive, shift.ValidFrom, shift.ValidUntil, shift.QualityCycleTemplateId);
    }
}

public class QualityCycleConflictException(string message) : Exception(message);
