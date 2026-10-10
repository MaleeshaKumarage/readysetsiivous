using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleClockTests
{
    private static readonly DateTime FromUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = new(2026, 10, 2, 23, 59, 59, DateTimeKind.Utc);

    private static Shift NewShift(Guid? templateId) =>
        Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(8, 0, 0), DailyEnd = new TimeSpan(16, 0, 0) },
            null, null, null, templateId);

    private static Employee NewEmployee() =>
        new Employee { Id = Guid.NewGuid(), Email = "e@x.fi", FirstName = "A", LastName = "B", IsActive = true };

    private static QualityCycleTemplate NewTemplate() =>
        QualityCycleTemplate.Create("T", new List<string> { "Item1" }, null, null, null);

    [Fact]
    public async Task Start_CreatesFormAndSetsStartedAt()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();

        var shiftRepo = new Mock<IShiftRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var qcRepo = new Mock<IQualityCycleRepository>();

        shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        empRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        shiftRepo.Setup(r => r.GetAssignmentAsync(shift.Id, emp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShiftAssignment.Create(shift.Id, emp.Id, null));
        qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityCycleForm?)null);

        var handler = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shiftRepo.Object, empRepo.Object, qcRepo.Object);
        var result = await handler.Handle(new StartQualityCycleFormCommand(shift.Id, emp.Id, FromUtc, FromUtc.AddHours(8)), CancellationToken.None);

        Assert.NotNull(result.StartedAtUtc);
        Assert.False(result.IsSubmitted);
        qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_ReusesExistingForm_DoesNotOverwriteStartedAt()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var existing = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc, template.Items, FromUtc.AddHours(8));
        existing.Start();

        var shiftRepo = new Mock<IShiftRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var qcRepo = new Mock<IQualityCycleRepository>();

        shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        empRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        shiftRepo.Setup(r => r.GetAssignmentAsync(shift.Id, emp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShiftAssignment.Create(shift.Id, emp.Id, null));
        qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shiftRepo.Object, empRepo.Object, qcRepo.Object);
        var result = await handler.Handle(new StartQualityCycleFormCommand(shift.Id, emp.Id, FromUtc, FromUtc.AddHours(8)), CancellationToken.None);

        Assert.Equal(existing.StartedAtUtc, result.StartedAtUtc);
    }

    [Fact]
    public async Task Start_WhenNotAssigned_ThrowsUnauthorized()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();

        var shiftRepo = new Mock<IShiftRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var qcRepo = new Mock<IQualityCycleRepository>();

        shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        empRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        shiftRepo.Setup(r => r.GetAssignmentAsync(shift.Id, emp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAssignment?)null);

        var handler = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shiftRepo.Object, empRepo.Object, qcRepo.Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new StartQualityCycleFormCommand(shift.Id, emp.Id, FromUtc, FromUtc.AddHours(8)), CancellationToken.None));
    }

    [Fact]
    public async Task End_SubmitsFormAndSetsEndedAt()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var form = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc, template.Items, FromUtc.AddHours(8));

        var qcRepo = new Mock<IQualityCycleRepository>();
        qcRepo.Setup(r => r.GetFormAsync(form.Id, It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var handler = new QualityCycleHandlers.EndQualityCycleFormCommandHandler(qcRepo.Object);
        var result = await handler.Handle(new EndQualityCycleFormCommand(form.Id, emp.Id, new List<QualityCycleFormItemDto> { new("Item1", true) }), CancellationToken.None);

        Assert.True(result.IsSubmitted);
        Assert.NotNull(result.EndedAtUtc);
        qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task End_WhenNotOwner_ThrowsUnauthorized()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var other = Guid.NewGuid();
        var form = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc, template.Items, FromUtc.AddHours(8));

        var qcRepo = new Mock<IQualityCycleRepository>();
        qcRepo.Setup(r => r.GetFormAsync(form.Id, It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var handler = new QualityCycleHandlers.EndQualityCycleFormCommandHandler(qcRepo.Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new EndQualityCycleFormCommand(form.Id, other, new List<QualityCycleFormItemDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task MyShifts_ReturnsOccurrencesWithForms()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var assignment = ShiftAssignment.Create(shift.Id, emp.Id, null);
        var form = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc, template.Items, FromUtc.AddHours(8));

        var shiftRepo = new Mock<IShiftRepository>();
        var qcRepo = new Mock<IQualityCycleRepository>();

        shiftRepo.Setup(r => r.ListAssignmentsByEmployeeAsync(emp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { assignment });
        shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityCycleForm?)form);

        var handler = new QualityCycleHandlers.GetMyShiftsQueryHandler(shiftRepo.Object, qcRepo.Object);
        var result = await handler.Handle(new GetMyShiftsQuery(emp.Id, FromUtc, ToUtc), CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.NotNull(r.Form));
    }
}
