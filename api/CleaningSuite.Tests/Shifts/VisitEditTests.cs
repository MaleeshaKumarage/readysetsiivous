using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

// One visit concept: generated and manual visits can be edited, cancelled and restored.
public sealed class VisitEditTests
{
    private static readonly DateTime Start = new(2026, 12, 7, 7, 0, 0, DateTimeKind.Utc);

    private static Shift OnCallShift() => Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "On call",
        new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, null, null);

    private static ShiftOccurrenceException ManualVisit(Shift shift) =>
        ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Added");

    private static Mock<IShiftRepository> Repository(Shift shift, ShiftOccurrenceException? visit = null)
    {
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        if (visit is not null) shifts.Setup(x => x.GetOccurrenceExceptionAsync(visit.Id, It.IsAny<CancellationToken>())).ReturnsAsync(visit);
        shifts.Setup(x => x.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ShiftOccurrenceException>());
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftAssignment>());
        shifts.Setup(x => x.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceAssignment>());
        return shifts;
    }

    // ---- Cancel a manual visit ----

    [Fact]
    public async Task Cancelling_a_manual_visit_keeps_a_copy_with_its_reason()
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift);
        var shifts = Repository(shift, visit);
        CancelledVisit? saved = null;
        shifts.Setup(x => x.CancelManualVisitAsync(It.IsAny<CancelledVisit>(), It.IsAny<CancellationToken>()))
            .Callback<CancelledVisit, CancellationToken>((c, _) => saved = c).Returns(Task.CompletedTask);

        var result = await new ShiftHandlers.CancelManualVisitCommandHandler(shifts.Object)
            .Handle(new CancelManualVisitCommand(shift.Id, visit.Id, ShiftOccurrenceExceptionStatus.CustomerCancelled, "Customer away"), default);

        Assert.NotNull(saved);
        Assert.Equal(visit.Id, saved!.Id);
        Assert.Equal(ShiftOccurrenceExceptionStatus.CustomerCancelled, saved.CancelStatus);
        Assert.Equal("Customer away", saved.Reason);
        Assert.True(result.IsCancelledManualVisit);
        Assert.False(result.CanAssign);
    }

    [Fact]
    public async Task A_scheduled_visit_is_not_cancelled_through_the_manual_visit_action()
    {
        var shift = OnCallShift();
        var moved = ShiftOccurrenceException.Create(shift.Id, Start, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.Moved, "Moved");
        var shifts = Repository(shift, moved);
        await Assert.ThrowsAsync<ShiftConflictException>(() => new ShiftHandlers.CancelManualVisitCommandHandler(shifts.Object)
            .Handle(new CancelManualVisitCommand(shift.Id, moved.Id, ShiftOccurrenceExceptionStatus.CompanyCancelled, "No"), default));
        shifts.Verify(x => x.CancelManualVisitAsync(It.IsAny<CancelledVisit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_partner_booking_cannot_be_cancelled_here()
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift); visit.PartnerRequestId = Guid.NewGuid();
        var shifts = Repository(shift, visit);
        await Assert.ThrowsAsync<ShiftConflictException>(() => new ShiftHandlers.CancelManualVisitCommandHandler(shifts.Object)
            .Handle(new CancelManualVisitCommand(shift.Id, visit.Id, ShiftOccurrenceExceptionStatus.CompanyCancelled, "No"), default));
    }

    [Fact]
    public async Task A_visit_with_started_work_cannot_be_cancelled()
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift);
        var shifts = Repository(shift, visit);
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(x => x.ListFormsAsync(shift.Id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QualityCycleForm> { new() { ShiftId = shift.Id, ShiftOccurrenceUtc = Start, StartedAtUtc = Start } });
        var ex = await Assert.ThrowsAsync<ShiftConflictException>(() => new ShiftHandlers.CancelManualVisitCommandHandler(shifts.Object, forms.Object)
            .Handle(new CancelManualVisitCommand(shift.Id, visit.Id, ShiftOccurrenceExceptionStatus.CompanyCancelled, "No"), default));
        Assert.Contains("Work has started", ex.Message);
    }

    [Fact]
    public void A_cancelled_copy_needs_a_cancel_status_and_a_reason()
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift);
        Assert.Throws<ArgumentException>(() => CancelledVisit.From(visit, ShiftOccurrenceExceptionStatus.Moved, "x", Start));
        Assert.Throws<ArgumentException>(() => CancelledVisit.From(visit, ShiftOccurrenceExceptionStatus.CompanyCancelled, " ", Start));
    }

    // ---- Restore a manual visit ----

    [Fact]
    public async Task Restoring_puts_the_same_visit_back_as_a_new_row()
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift); visit.Sequence = 7; visit.Version = Guid.NewGuid();
        var cancelled = CancelledVisit.From(visit, ShiftOccurrenceExceptionStatus.CompanyCancelled, "Rain", Start);
        var shifts = Repository(shift);
        shifts.Setup(x => x.GetCancelledVisitAsync(visit.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cancelled);
        CancelledVisit? restored = null;
        shifts.Setup(x => x.RestoreManualVisitAsync(It.IsAny<CancelledVisit>(), It.IsAny<CancellationToken>()))
            .Callback<CancelledVisit, CancellationToken>((c, _) => restored = c).Returns(Task.CompletedTask);

        var result = await new ShiftHandlers.RestoreManualVisitCommandHandler(shifts.Object)
            .Handle(new RestoreManualVisitCommand(shift.Id, visit.Id), default);

        Assert.NotNull(restored);
        Assert.Equal(visit.Id, restored!.Visit.Id);
        Assert.Equal(7, restored.Visit.Sequence);
        Assert.Equal(Guid.Empty, restored.Visit.Version);
        Assert.Equal(ShiftOccurrenceExceptionStatus.Extra, result.Status);
    }

    [Fact]
    public async Task Restoring_on_a_main_shift_is_refused_when_another_visit_has_the_same_start()
    {
        var shift = OnCallShift(); shift.IsBranchMain = true;
        var visit = ManualVisit(shift);
        var cancelled = CancelledVisit.From(visit, ShiftOccurrenceExceptionStatus.CompanyCancelled, "Rain", Start);
        var other = ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(1), ShiftOccurrenceExceptionStatus.Extra, "Other");
        var shifts = Repository(shift);
        shifts.Setup(x => x.GetCancelledVisitAsync(visit.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cancelled);
        shifts.Setup(x => x.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { other });
        await Assert.ThrowsAsync<ShiftConflictException>(() => new ShiftHandlers.RestoreManualVisitCommandHandler(shifts.Object)
            .Handle(new RestoreManualVisitCommand(shift.Id, visit.Id), default));
        shifts.Verify(x => x.RestoreManualVisitAsync(It.IsAny<CancelledVisit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_inactive_shift_cannot_have_visits_restored()
    {
        var shift = OnCallShift(); shift.IsActive = false;
        var shifts = Repository(shift);
        await Assert.ThrowsAsync<ShiftConflictException>(() => new ShiftHandlers.RestoreManualVisitCommandHandler(shifts.Object)
            .Handle(new RestoreManualVisitCommand(shift.Id, Guid.NewGuid()), default));
    }

    // ---- Notes on an existing visit ----

    [Theory]
    [InlineData("  Use the side door  ", "Use the side door")]
    [InlineData("", null)]
    public async Task Editing_a_manual_visit_sets_or_clears_its_notes(string notes, string? expected)
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift); visit.Notes = "Old note";
        var shifts = Repository(shift, visit);
        var handler = new ShiftHandlers.SaveShiftOccurrenceExceptionCommandHandler(shifts.Object, new Mock<IEmployeeRepository>().Object);
        await handler.Handle(new SaveShiftOccurrenceExceptionCommand(shift.Id, visit.Id, null, Start, Start.AddHours(2),
            ShiftOccurrenceExceptionStatus.Extra, "Edited", Notes: notes), default);
        Assert.Equal(expected, visit.Notes);
        shifts.Verify(x => x.SaveOccurrenceExceptionAsync(visit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Editing_without_notes_keeps_the_existing_notes()
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift); visit.Notes = "Keep me";
        var shifts = Repository(shift, visit);
        var handler = new ShiftHandlers.SaveShiftOccurrenceExceptionCommandHandler(shifts.Object, new Mock<IEmployeeRepository>().Object);
        await handler.Handle(new SaveShiftOccurrenceExceptionCommand(shift.Id, visit.Id, null, Start.AddHours(1), Start.AddHours(3),
            ShiftOccurrenceExceptionStatus.Extra, "Later"), default);
        Assert.Equal("Keep me", visit.Notes);
        Assert.Equal(Start.AddHours(1), visit.StartUtc);
    }

    // ---- Visit list ----

    [Fact]
    public async Task The_visit_list_includes_cancelled_manual_visits_so_they_can_be_restored()
    {
        var shift = OnCallShift(); var visit = ManualVisit(shift); visit.Notes = "Gate code 12";
        var cancelled = CancelledVisit.From(visit, ShiftOccurrenceExceptionStatus.CustomerCancelled, "Away", Start);
        var shifts = Repository(shift);
        shifts.Setup(x => x.ListCancelledVisitsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { cancelled });

        var list = await new ShiftHandlers.GetShiftOccurrencesQueryHandler(shifts.Object)
            .Handle(new GetShiftOccurrencesQuery(shift.Id, new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)), default);

        var item = Assert.Single(list);
        Assert.True(item.IsCancelledManualVisit);
        Assert.Equal(ShiftOccurrenceExceptionStatus.CustomerCancelled, item.Status);
        Assert.Equal("Gate code 12", item.VisitNotes);
        Assert.Equal(visit.Id, item.ExceptionId);
    }
}
