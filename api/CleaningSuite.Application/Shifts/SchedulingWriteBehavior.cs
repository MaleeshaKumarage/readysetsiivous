using CleaningSuite.Application.QualityCycle;
using MediatR;

namespace CleaningSuite.Application.Shifts;

/// <summary>Legacy scheduling writes share the same cross-replica reservation as clock-in and visit assignment.</summary>
public sealed class SchedulingWriteBehavior<TRequest, TResponse>(IShiftWorkReservation reservation,
    IShiftRepository shifts, IQualityCycleRepository forms) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var write = request is UpdateShiftCommand or DeactivateShiftCommand or ReactivateShiftCommand
            or DeleteShiftCommand or AssignEmployeeToShiftCommand or RemoveEmployeeFromShiftCommand
            or SaveShiftOccurrenceExceptionCommand or DeleteShiftOccurrenceExceptionCommand;
        if (!write) return await next();
        await using var lease = await reservation.AcquireAsync(ct);
        Guid? shiftId = null; DateTime? start = null; DateTime? original = null;
        if (request is SaveShiftOccurrenceExceptionCommand save)
        { shiftId = save.ShiftId; start = save.StartUtc; original = save.OriginalStartUtc; }
        if (request is DeleteShiftOccurrenceExceptionCommand remove)
        {
            var item = await shifts.GetOccurrenceExceptionAsync(remove.Id, ct);
            if (item is not null && item.ShiftId == remove.ShiftId)
            { shiftId = item.ShiftId; start = item.StartUtc; original = item.OriginalStartUtc; }
        }
        if (shiftId.HasValue)
        {
            var evidence = await forms.ListFormsAsync(shiftId, null, null, ct) ?? [];
            if (evidence.Any(f => (f.IsSubmitted || f.StartedAtUtc.HasValue)
                && (f.ShiftOccurrenceUtc == start || f.ShiftOccurrenceUtc == original)))
                throw new ShiftConflictException("Started or completed visits cannot be moved, cancelled or restored.");
        }
        return await next();
    }
}
