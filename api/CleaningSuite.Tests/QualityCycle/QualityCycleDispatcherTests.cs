using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleDispatcherTests
{
    private readonly Mock<IShiftRepository> _shiftRepo = new();
    private readonly Mock<IQualityCycleRepository> _qcRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly QualityCycleDispatcher _dispatcher;

    public QualityCycleDispatcherTests()
    {
        _dispatcher = new QualityCycleDispatcher(
            _shiftRepo.Object, _qcRepo.Object, _employeeRepo.Object, _emailSender.Object);
    }

    private static Shift NewShift(Guid? templateId) =>
        Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(8, 0, 0), DailyEnd = new TimeSpan(16, 0, 0) },
            null, null, null, templateId);

    private static Employee NewEmployee() =>
        new Employee { Id = Guid.NewGuid(), Email = "e@x.fi", FirstName = "A", LastName = "B", IsActive = true };

    private static IReadOnlyList<ShiftOccurrence> OneOccurrence() =>
        new List<ShiftOccurrence>
        {
            new()
            {
                StartUtc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            }
        };

    private static QualityCycleTemplate NewTemplate() =>
        QualityCycleTemplate.Create("T", new List<string> { "Item1" }, null, null, null);

    [Fact]
    public async Task Dispatch_CreatesFormAndSendsEmail()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();

        _qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _shiftRepo.Setup(r => r.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { ShiftAssignment.Create(shift.Id, emp.Id, null) });
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);

        var count = await _dispatcher.DispatchAsync(shift, OneOccurrence(), CancellationToken.None);

        Assert.Equal(1, count);
        _qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(e => e.SendAsync(emp.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_SkipsExistingForm()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();

        _qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _shiftRepo.Setup(r => r.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { ShiftAssignment.Create(shift.Id, emp.Id, null) });
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityCycleForm());

        var count = await _dispatcher.DispatchAsync(shift, OneOccurrence(), CancellationToken.None);

        Assert.Equal(0, count);
        _qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailSender.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_ReturnsZero_WhenNoTemplate()
    {
        var shift = NewShift(null);

        var count = await _dispatcher.DispatchAsync(shift, OneOccurrence(), CancellationToken.None);

        Assert.Equal(0, count);
        _qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_SkipsInactiveEmployee()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        emp.IsActive = false;

        _qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _shiftRepo.Setup(r => r.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { ShiftAssignment.Create(shift.Id, emp.Id, null) });
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);

        var count = await _dispatcher.DispatchAsync(shift, OneOccurrence(), CancellationToken.None);

        Assert.Equal(0, count);
        _qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
