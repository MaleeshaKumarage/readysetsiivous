using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class DeleteShiftHistoryTests
{
    private static (Mock<IShiftRepository> shifts, Shift shift, Mock<IQualityCycleRepository> forms) Setup(IReadOnlyList<QualityCycleForm> visits)
    {
        var shift = new Shift { Id = Guid.NewGuid(), IsActive = false };
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(x => x.ListFormsAsync(shift.Id, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync(visits);
        return (shifts, shift, forms);
    }

    [Fact]
    public async Task Deactivated_shift_with_a_submitted_visit_is_kept_and_the_message_says_why()
    {
        var (shifts, shift, forms) = Setup([new QualityCycleForm { ShiftId = Guid.NewGuid(), IsSubmitted = true }]);
        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            new ShiftHandlers.DeleteShiftCommandHandler(shifts.Object, forms.Object).Handle(new(shift.Id), default));
        Assert.Contains("Keep it deactivated", ex.Message);
        shifts.Verify(x => x.DeleteAsync(shift.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivated_shift_with_a_started_visit_is_kept()
    {
        var (shifts, shift, forms) = Setup([new QualityCycleForm { ShiftId = Guid.NewGuid(), StartedAtUtc = DateTime.UtcNow }]);
        await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            new ShiftHandlers.DeleteShiftCommandHandler(shifts.Object, forms.Object).Handle(new(shift.Id), default));
        shifts.Verify(x => x.DeleteAsync(shift.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivated_shift_with_only_unstarted_visits_is_deleted()
    {
        var (shifts, shift, forms) = Setup([new QualityCycleForm { ShiftId = Guid.NewGuid(), IsSubmitted = false }]);
        await new ShiftHandlers.DeleteShiftCommandHandler(shifts.Object, forms.Object).Handle(new(shift.Id), default);
        shifts.Verify(x => x.DeleteAsync(shift.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
