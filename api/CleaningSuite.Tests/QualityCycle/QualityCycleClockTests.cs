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
    private sealed class FixedClock(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
    }

    private static Shift NewShift(Guid? templateId) =>
        Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(8, 0, 0), DailyEnd = new TimeSpan(16, 0, 0) },
            null, null, null, templateId);

    private static ShiftAssignment ExistingAssignment(Guid shift, Guid employee) { var assignment = ShiftAssignment.Create(shift, employee, null); assignment.AssignedAtUtc = FromUtc.AddDays(-1); return assignment; }

    private static Employee NewEmployee(string role = "employee") =>
        new Employee { Id = Guid.NewGuid(), Email = "e@x.fi", FirstName = "A", LastName = "B", IsActive = true, Role = role };

    private static QualityCycleTemplate NewTemplate() =>
        QualityCycleTemplate.Create("T", new List<string> { "Item1" }, null, null, null);

    [Theory]
    [InlineData(DateTimeKind.Utc, "employee")] [InlineData(DateTimeKind.Utc, "admin")] [InlineData(DateTimeKind.Utc, "manager")]
    [InlineData(DateTimeKind.Local, "employee")] [InlineData(DateTimeKind.Local, "admin")] [InlineData(DateTimeKind.Local, "manager")]
    [InlineData(DateTimeKind.Unspecified, "employee")] [InlineData(DateTimeKind.Unspecified, "admin")] [InlineData(DateTimeKind.Unspecified, "manager")]
    public async Task Start_CreatesFormAndSetsStartedAt(DateTimeKind kind, string role)
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee(role);

        var shiftRepo = new Mock<IShiftRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var qcRepo = new Mock<IQualityCycleRepository>();

        shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        empRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        shiftRepo.Setup(r => r.GetAssignmentAsync(shift.Id, emp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingAssignment(shift.Id, emp.Id));
        qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityCycleForm?)null);

        var handler = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shiftRepo.Object, empRepo.Object, qcRepo.Object, new FixedClock(FromUtc.AddHours(8)));
        qcRepo.Setup(r => r.TryCreateFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var start = kind == DateTimeKind.Local ? FromUtc.AddHours(8).ToLocalTime() : DateTime.SpecifyKind(FromUtc.AddHours(8), kind);
        var end = kind == DateTimeKind.Local ? FromUtc.AddHours(16).ToLocalTime() : DateTime.SpecifyKind(FromUtc.AddHours(16), kind);
        var result = await handler.Handle(new StartQualityCycleFormCommand(shift.Id, emp.Id, start, end), CancellationToken.None);

        Assert.NotNull(result.StartedAtUtc);
        Assert.False(result.IsSubmitted);
        qcRepo.Verify(r => r.TryCreateFormAsync(It.Is<QualityCycleForm>(f => f.Id == QualityCycleDispatcher.TaskId(shift.Id, emp.Id, FromUtc.AddHours(8))), It.IsAny<CancellationToken>()), Times.Once);
        qcRepo.Verify(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.Is<DateTime>(t => t.Kind == DateTimeKind.Utc && t == FromUtc.AddHours(8)), It.IsAny<CancellationToken>()), Times.Once);
        qcRepo.Verify(r => r.TryCreateFormAsync(It.Is<QualityCycleForm>(f => f.ShiftOccurrenceUtc.Kind == DateTimeKind.Utc && f.ShiftOccurrenceUtc == FromUtc.AddHours(8)), It.IsAny<CancellationToken>()), Times.Once);
        qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(2)] // Before the scheduled start
    [InlineData(392)] // October 17: record an October 1 visit without rewriting scheduled time
    [InlineData(20)] // After the scheduled end, but still on the scheduled tenant date
    public async Task Start_AllowsStartingOutsideScheduledHoursOnScheduledDate_AndRecordsActualTime(int actualHourUtc)
    {
        var shift = NewShift(null); var employee = NewEmployee(); var clock = new FixedClock(FromUtc.AddHours(actualHourUtc));
        var shifts = new Mock<IShiftRepository>(); var employees = new Mock<IEmployeeRepository>(); var forms = new Mock<IQualityCycleRepository>();
        shifts.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        employees.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        shifts.Setup(r => r.GetAssignmentAsync(shift.Id, employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingAssignment(shift.Id, employee.Id));
        forms.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((QualityCycleForm?)null);
        forms.Setup(r => r.TryCreateFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shifts.Object, employees.Object, forms.Object, clock)
            .Handle(new(shift.Id, employee.Id, FromUtc.AddHours(8), FromUtc.AddHours(16)), default);

        Assert.Equal(FromUtc.AddHours(actualHourUtc), result.StartedAtUtc);
    }

    [Fact]
    public async Task Start_Allows_Backdated_Visit_Assigned_Later_And_Keeps_Scheduled_And_Actual_Times_Separate()
    {
        var shift = NewShift(null); shift.ValidFrom = FromUtc;
        var employee = NewEmployee(); var actual = FromUtc.AddDays(16).AddHours(12);
        var assignment = ShiftAssignment.Create(shift.Id, employee.Id, null); assignment.AssignedAtUtc = actual;
        var shifts = new Mock<IShiftRepository>(); var employees = new Mock<IEmployeeRepository>(); var forms = new Mock<IQualityCycleRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shifts.Setup(x => x.GetAssignmentAsync(shift.Id, employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);
        employees.Setup(x => x.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        forms.Setup(x => x.TryCreateFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var result = await new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shifts.Object, employees.Object, forms.Object, new FixedClock(actual))
            .Handle(new(shift.Id, employee.Id, FromUtc.AddHours(8), FromUtc.AddHours(16)), default);
        Assert.Equal(FromUtc.AddHours(8), result.ShiftOccurrenceUtc);
        Assert.Equal(actual, result.StartedAtUtc);
    }

    [Fact]
    public async Task Start_RejectsFutureScheduledTenantDate()
    {
        var shift = NewShift(null); var employee = NewEmployee(); var clock = new FixedClock(FromUtc.AddHours(2));
        var shifts = new Mock<IShiftRepository>(); var employees = new Mock<IEmployeeRepository>(); var forms = new Mock<IQualityCycleRepository>();
        shifts.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        employees.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        shifts.Setup(r => r.GetAssignmentAsync(shift.Id, employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingAssignment(shift.Id, employee.Id));
        var handler = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shifts.Object, employees.Object, forms.Object, clock);

        var error = await Assert.ThrowsAsync<QualityCycleConflictException>(() => handler.Handle(
            new(shift.Id, employee.Id, FromUtc.AddDays(1).AddHours(8), FromUtc.AddDays(1).AddHours(16)), default));

        Assert.Equal("Future shifts cannot be started before their scheduled date.", error.Message);
        forms.Verify(r => r.GetFormByShiftOccurrenceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(5)] // Before the scheduled end
    [InlineData(392)] // October 17: record an October 1 visit without rewriting scheduled time
    [InlineData(20)] // After the scheduled end
    public async Task End_AllowsEndingOutsideScheduledHoursAfterActualStart_AndRecordsActualTime(int actualHourUtc)
    {
        var shift = NewShift(null); var employee = NewEmployee(); var clock = new FixedClock(FromUtc.AddHours(actualHourUtc));
        var form = QualityCycleForm.Create(shift.Id, shift.Name, employee.Id, "A B", Guid.Empty, "Shift activities", FromUtc.AddHours(8), new(), FromUtc.AddHours(16));
        form.Start(FromUtc.AddHours(2));
        var repository = new Mock<IQualityCycleRepository>(); repository.Setup(r => r.GetFormAsync(form.Id, It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var result = await new QualityCycleHandlers.EndQualityCycleFormCommandHandler(repository.Object, NoopUploadContentValidator.Instance, clock)
            .Handle(new(form.Id, employee.Id, new()), default);

        Assert.Equal(FromUtc.AddHours(2), result.StartedAtUtc);
        Assert.Equal(FromUtc.AddHours(actualHourUtc), result.EndedAtUtc);
    }

    [Fact]
    public async Task Start_ReusesExistingForm_DoesNotOverwriteStartedAt()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var existing = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc.AddHours(8), template.Items, FromUtc.AddHours(16));
        existing.Start(FromUtc.AddHours(9));

        var shiftRepo = new Mock<IShiftRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var qcRepo = new Mock<IQualityCycleRepository>();

        shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        empRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        shiftRepo.Setup(r => r.GetAssignmentAsync(shift.Id, emp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingAssignment(shift.Id, emp.Id));
        qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shiftRepo.Object, empRepo.Object, qcRepo.Object, new FixedClock(FromUtc.AddHours(10)));
        var result = await handler.Handle(new StartQualityCycleFormCommand(shift.Id, emp.Id, FromUtc.AddHours(8), FromUtc.AddHours(16)), CancellationToken.None);

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

        var handler = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shiftRepo.Object, empRepo.Object, qcRepo.Object, new FixedClock(FromUtc.AddHours(10)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new StartQualityCycleFormCommand(shift.Id, emp.Id, FromUtc.AddHours(8), FromUtc.AddHours(16)), CancellationToken.None));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task End_SubmitsSelectedActivitiesAndSetsEndedAt(bool first, bool second)
    {
        var template = NewTemplate();
        template.Items.Add("Item2");
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var form = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc.AddHours(8), template.Items, FromUtc.AddHours(16));
        form.Start();

        var qcRepo = new Mock<IQualityCycleRepository>();
        qcRepo.Setup(r => r.GetFormAsync(form.Id, It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var handler = new QualityCycleHandlers.EndQualityCycleFormCommandHandler(qcRepo.Object, NoopUploadContentValidator.Instance);
        var result = await handler.Handle(new EndQualityCycleFormCommand(form.Id, emp.Id, new List<QualityCycleFormItemDto> { new("Item1", first), new("Item2", second) }), CancellationToken.None);

        Assert.True(result.IsSubmitted);
        Assert.NotNull(result.EndedAtUtc);
        Assert.Equal(first, result.Items[0].IsChecked); Assert.Equal(second, result.Items[1].IsChecked);
        qcRepo.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task End_BeforeStartRejectsWithoutWriting()
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.Empty, "Shift activities", FromUtc, new());
        var repository = new Mock<IQualityCycleRepository>(); repository.Setup(r => r.GetFormAsync(form.Id, It.IsAny<CancellationToken>())).ReturnsAsync(form);
        await Assert.ThrowsAsync<QualityCycleConflictException>(() => new QualityCycleHandlers.EndQualityCycleFormCommandHandler(repository.Object, NoopUploadContentValidator.Instance)
            .Handle(new(form.Id, form.EmployeeId, new()), default));
        repository.Verify(r => r.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StartWithoutActiveChecklistStillCreatesOwnedTaskThatCanEnd(bool inactiveTemplate)
    {
        var employee = NewEmployee(); var template = NewTemplate(); template.IsActive = false;
        var shift = NewShift(inactiveTemplate ? template.Id : null);
        var shifts = new Mock<IShiftRepository>(); var employees = new Mock<IEmployeeRepository>(); var forms = new Mock<IQualityCycleRepository>();
        shifts.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shifts.Setup(r => r.GetAssignmentAsync(shift.Id, employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingAssignment(shift.Id, employee.Id));
        employees.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        forms.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        QualityCycleForm? created = null;
        forms.Setup(r => r.TryCreateFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>())).Callback<QualityCycleForm,CancellationToken>((f,_) => created=f).ReturnsAsync(true);
        var dto = await new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shifts.Object, employees.Object, forms.Object, new FixedClock(FromUtc.AddHours(10)))
            .Handle(new(shift.Id, employee.Id, FromUtc.AddHours(8), FromUtc.AddHours(16)), default);
        Assert.Empty(dto.Items); Assert.NotNull(dto.StartedAtUtc); Assert.Equal(Guid.Empty, dto.TemplateId);
        Assert.Equal(employee.Id, created!.EmployeeId); Assert.Equal(shift.CompanyId, created.CompanyId); Assert.Equal(shift.BranchId, created.BranchId);
        forms.Setup(r => r.GetFormAsync(created.Id, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        var ended = await new QualityCycleHandlers.EndQualityCycleFormCommandHandler(forms.Object, NoopUploadContentValidator.Instance).Handle(new(created.Id, employee.Id, new()), default);
        Assert.True(ended.IsSubmitted); Assert.Empty(ended.Items);
    }

    [Fact]
    public async Task End_WhenNotOwner_ThrowsUnauthorized()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var other = Guid.NewGuid();
        var form = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc.AddHours(8), template.Items, FromUtc.AddHours(16));

        var qcRepo = new Mock<IQualityCycleRepository>();
        qcRepo.Setup(r => r.GetFormAsync(form.Id, It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var handler = new QualityCycleHandlers.EndQualityCycleFormCommandHandler(qcRepo.Object, NoopUploadContentValidator.Instance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new EndQualityCycleFormCommand(form.Id, other, new List<QualityCycleFormItemDto>()), CancellationToken.None));
    }

    [Fact]
    public async Task MyShifts_DoesNotRelabelAnExistingFormAsAnotherOccurrence()
    {
        var template = NewTemplate();
        var shift = NewShift(template.Id);
        var emp = NewEmployee();
        var assignment = ShiftAssignment.Create(shift.Id, emp.Id, null); assignment.AssignedAtUtc = FromUtc.AddDays(-1);
        var form = QualityCycleForm.Create(shift.Id, shift.Name, emp.Id, "A B", template.Id, template.Title, FromUtc.AddHours(8), template.Items, FromUtc.AddHours(16));

        var shiftRepo = new Mock<IShiftRepository>();
        var qcRepo = new Mock<IQualityCycleRepository>();

        shiftRepo.Setup(r => r.ListAssignmentsByEmployeeAsync(emp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { assignment });
        shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityCycleForm?)form);

        var handler = new QualityCycleHandlers.GetMyShiftsQueryHandler(shiftRepo.Object, qcRepo.Object);
        var result = await handler.Handle(new GetMyShiftsQuery(emp.Id, FromUtc, ToUtc), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(form.Id, result[0].Form!.Id);
        Assert.Null(result[1].Form);
    }

    [Fact]
    public async Task MyShifts_ForeignEmployeeFormCannotEscapeThroughOwnedOccurrence()
    {
        var template = NewTemplate(); var shift = NewShift(template.Id); var employee = NewEmployee();
        var shifts = new Mock<IShiftRepository>(); var forms = new Mock<IQualityCycleRepository>();
        shifts.Setup(r => r.ListAssignmentsByEmployeeAsync(employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ExistingAssignment(shift.Id, employee.Id) });
        shifts.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        var foreign = QualityCycleForm.Create(shift.Id, shift.Name, Guid.NewGuid(), "Other cleaner", template.Id, template.Title, FromUtc.AddHours(8), template.Items);
        forms.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var result = await new QualityCycleHandlers.GetMyShiftsQueryHandler(shifts.Object, forms.Object).Handle(new(employee.Id, FromUtc, ToUtc), default);
        Assert.NotEmpty(result); Assert.All(result, r => Assert.Null(r.Form));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(33)]
    public async Task MyShifts_InvalidWindowIsRejectedBeforeAssignmentsAreRead(int days)
    {
        var shifts = new Mock<IShiftRepository>();
        var handler = new QualityCycleHandlers.GetMyShiftsQueryHandler(shifts.Object, Mock.Of<IQualityCycleRepository>());
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => handler.Handle(new(Guid.NewGuid(), FromUtc, FromUtc.AddDays(days)), default));
        Assert.Empty(shifts.Invocations);
    }
}
