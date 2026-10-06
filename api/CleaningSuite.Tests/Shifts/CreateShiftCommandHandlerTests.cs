using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using FluentValidation.TestHelper;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class CreateShiftCommandHandlerTests
{
    private readonly Mock<IShiftRepository> _shiftRepoMock = new();
    private readonly Mock<ICompanyRepository> _companyRepoMock = new();
    private readonly Mock<IBranchRepository> _branchRepoMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepoMock = new();

    private readonly ShiftSchedule _validSchedule = new()
    {
        Type = ShiftScheduleType.DailySameTime,
        DailyStart = new TimeSpan(8, 0, 0),
        DailyEnd = new TimeSpan(16, 0, 0)
    };

    [Fact]
    public async Task CreateShift_Succeeds_WhenCompanyAndBranchAreValid()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        _companyRepoMock.Setup(r => r.GetAsync(companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Company.Create("1234567-8", "Test Company", null, null, null));

        _branchRepoMock.Setup(r => r.GetAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = branchId, CompanyId = companyId, Name = "Branch 1" });

        _shiftRepoMock.Setup(r => r.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ShiftHandlers.CreateShiftCommandHandler(
            _shiftRepoMock.Object, _companyRepoMock.Object, _branchRepoMock.Object);

        var command = new CreateShiftCommand(companyId, branchId, "Morning Shift", _validSchedule, "Notes");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Morning Shift", result.Name);
        Assert.Equal(companyId, result.CompanyId);
        Assert.Equal(branchId, result.BranchId);
        _shiftRepoMock.Verify(r => r.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateShift_ThrowsNotFoundException_WhenCompanyDoesNotExist()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        _companyRepoMock.Setup(r => r.GetAsync(companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Company?)null);

        var handler = new ShiftHandlers.CreateShiftCommandHandler(
            _shiftRepoMock.Object, _companyRepoMock.Object, _branchRepoMock.Object);

        var command = new CreateShiftCommand(companyId, branchId, "Shift", _validSchedule);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateShift_ThrowsNotFoundException_WhenBranchDoesNotExist()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        _companyRepoMock.Setup(r => r.GetAsync(companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Company.Create("1234567-8", "Company", null, null, null));
        _branchRepoMock.Setup(r => r.GetAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Branch?)null);

        var handler = new ShiftHandlers.CreateShiftCommandHandler(
            _shiftRepoMock.Object, _companyRepoMock.Object, _branchRepoMock.Object);

        var command = new CreateShiftCommand(companyId, branchId, "Shift", _validSchedule);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateShift_ThrowsNotFoundException_WhenBranchCompanyIdMismatches()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        _companyRepoMock.Setup(r => r.GetAsync(companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Company.Create("1234567-8", "Company", null, null, null));
        _branchRepoMock.Setup(r => r.GetAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = branchId, CompanyId = otherCompanyId, Name = "Branch" });

        var handler = new ShiftHandlers.CreateShiftCommandHandler(
            _shiftRepoMock.Object, _companyRepoMock.Object, _branchRepoMock.Object);

        var command = new CreateShiftCommand(companyId, branchId, "Shift", _validSchedule);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateShift_Succeeds_WhenShiftExists()
    {
        var shiftId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var shift = Shift.Create(companyId, branchId, "Old Name", _validSchedule, null, null, null);

        _shiftRepoMock.Setup(r => r.GetAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        _shiftRepoMock.Setup(r => r.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new ShiftHandlers.UpdateShiftCommandHandler(_shiftRepoMock.Object);
        var command = new UpdateShiftCommand(shiftId, companyId, branchId, "New Name", _validSchedule, "Note", true, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("New Name", result.Name);
        Assert.Equal("Note", result.Notes);
    }

    [Fact]
    public async Task UpdateShift_ThrowsNotFoundException_WhenShiftDoesNotExist()
    {
        _shiftRepoMock.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);

        var handler = new ShiftHandlers.UpdateShiftCommandHandler(_shiftRepoMock.Object);
        var command = new UpdateShiftCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task DeactivateShift_Succeeds_And_DeactivatesShift()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule, null, null, null);
        _shiftRepoMock.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);

        var handler = new ShiftHandlers.DeactivateShiftCommandHandler(_shiftRepoMock.Object);
        await handler.Handle(new DeactivateShiftCommand(shift.Id), CancellationToken.None);

        Assert.False(shift.IsActive);
        _shiftRepoMock.Verify(r => r.SaveAsync(shift, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateShift_ThrowsNotFoundException_WhenShiftNotFound()
    {
        _shiftRepoMock.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);

        var handler = new ShiftHandlers.DeactivateShiftCommandHandler(_shiftRepoMock.Object);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new DeactivateShiftCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task ListShifts_ReturnsAllShifts()
    {
        var shift1 = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift 1", _validSchedule, null, null, null);
        var shift2 = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift 2", _validSchedule, null, null, null);

        _shiftRepoMock.Setup(r => r.ListAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { shift1, shift2 });

        var handler = new ShiftHandlers.ListShiftsQueryHandler(_shiftRepoMock.Object);
        var result = await handler.Handle(new ListShiftsQuery(null, null), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetShift_ReturnsShift_Or_ThrowsNotFoundException()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift 1", _validSchedule, null, null, null);
        _shiftRepoMock.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);

        var handler = new ShiftHandlers.GetShiftQueryHandler(_shiftRepoMock.Object);
        var dto = await handler.Handle(new GetShiftQuery(shift.Id), CancellationToken.None);

        Assert.Equal(shift.Id, dto.Id);

        _shiftRepoMock.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetShiftQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task GetShiftOccurrences_ReturnsOccurrences_Or_ThrowsNotFoundException()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule, null, null, null);
        _shiftRepoMock.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);

        var handler = new ShiftHandlers.GetShiftOccurrencesQueryHandler(_shiftRepoMock.Object);
        var from = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Unspecified);
        var to = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);

        var occurrences = await handler.Handle(new GetShiftOccurrencesQuery(shift.Id, from, to), CancellationToken.None);

        Assert.Equal(3, occurrences.Count);

        _shiftRepoMock.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetShiftOccurrencesQuery(Guid.NewGuid(), from, to), CancellationToken.None));
    }

    [Fact]
    public void AssignEmployeeToShiftCommandHandler_Throws_OnInvalidMaxConflictWindowDays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShiftHandlers.AssignEmployeeToShiftCommandHandler(
            _shiftRepoMock.Object, _employeeRepoMock.Object, maxConflictWindowDays: 0));
    }

    [Fact]
    public async Task AssignEmployeeToShift_Succeeds_And_CreatesAssignment()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule, null, null, null);
        var empId = Guid.NewGuid();
        var emp = new Employee { Id = empId, Email = "test@test.fi", FirstName = "Test", LastName = "User", Phone = "0401234567", Role = Employee.RoleEmployee, IsActive = true };

        _shiftRepoMock.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        _employeeRepoMock.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _shiftRepoMock.Setup(r => r.ListAssignmentsByEmployeeAsync(empId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment>());
        _shiftRepoMock.Setup(r => r.GetAssignmentAsync(shift.Id, empId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAssignment?)null);

        var handler = new ShiftHandlers.AssignEmployeeToShiftCommandHandler(_shiftRepoMock.Object, _employeeRepoMock.Object);
        var result = await handler.Handle(new AssignEmployeeToShiftCommand(shift.Id, empId, "Primary"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(shift.Id, result.ShiftId);
        Assert.Equal(empId, result.EmployeeId);
        Assert.Equal("Primary", result.Note);
    }

    [Fact]
    public async Task AssignEmployeeToShift_UpdatesExistingAssignment()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule, null, null, null);
        var empId = Guid.NewGuid();
        var emp = new Employee { Id = empId, Email = "test@test.fi", FirstName = "Test", LastName = "User", Phone = "0401234567", Role = Employee.RoleEmployee, IsActive = true };
        var existingAssignment = ShiftAssignment.Create(shift.Id, empId, "Old Note");

        _shiftRepoMock.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        _employeeRepoMock.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _shiftRepoMock.Setup(r => r.ListAssignmentsByEmployeeAsync(empId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { existingAssignment });
        _shiftRepoMock.Setup(r => r.GetAssignmentAsync(shift.Id, empId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAssignment);

        var handler = new ShiftHandlers.AssignEmployeeToShiftCommandHandler(_shiftRepoMock.Object, _employeeRepoMock.Object);
        var result = await handler.Handle(new AssignEmployeeToShiftCommand(shift.Id, empId, "Updated Note"), CancellationToken.None);

        Assert.True(result.IsActive);
        Assert.Equal("Updated Note", result.Note);
    }

    [Fact]
    public async Task AssignEmployeeToShift_ThrowsConflictException_WhenOverlappingShiftExists()
    {
        var shift1 = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift 1", _validSchedule, null, null, null);
        var shift2 = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift 2", _validSchedule, null, null, null);
        var empId = Guid.NewGuid();
        var emp = new Employee { Id = empId, Email = "test@test.fi", FirstName = "Test", LastName = "User", Phone = "0401234567", Role = Employee.RoleEmployee, IsActive = true };
        var existingAssignment = ShiftAssignment.Create(shift1.Id, empId, null);

        _shiftRepoMock.Setup(r => r.GetAsync(shift2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift2);
        _employeeRepoMock.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _shiftRepoMock.Setup(r => r.ListAssignmentsByEmployeeAsync(empId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { existingAssignment });
        _shiftRepoMock.Setup(r => r.ListByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(shift1.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { shift1 });

        var handler = new ShiftHandlers.AssignEmployeeToShiftCommandHandler(_shiftRepoMock.Object, _employeeRepoMock.Object);

        await Assert.ThrowsAsync<ShiftConflictException>(() =>
            handler.Handle(new AssignEmployeeToShiftCommand(shift2.Id, empId), CancellationToken.None));
    }

    [Fact]
    public async Task AssignEmployeeToShift_ThrowsNotFoundException_WhenShiftOrEmployeeNotFound()
    {
        _shiftRepoMock.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);
        var handler = new ShiftHandlers.AssignEmployeeToShiftCommandHandler(_shiftRepoMock.Object, _employeeRepoMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new AssignEmployeeToShiftCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule, null, null, null);
        _shiftRepoMock.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        _employeeRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new AssignEmployeeToShiftCommand(shift.Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveEmployeeFromShift_DeactivatesAssignment_WhenFound()
    {
        var shiftId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var assignment = ShiftAssignment.Create(shiftId, empId, null);

        _shiftRepoMock.Setup(r => r.GetAssignmentAsync(shiftId, empId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignment);

        var handler = new ShiftHandlers.RemoveEmployeeFromShiftCommandHandler(_shiftRepoMock.Object);
        await handler.Handle(new RemoveEmployeeFromShiftCommand(shiftId, empId), CancellationToken.None);

        Assert.False(assignment.IsActive);
        _shiftRepoMock.Verify(r => r.SaveAssignmentAsync(assignment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveEmployeeFromShift_DoesNothing_WhenAssignmentNotFound()
    {
        _shiftRepoMock.Setup(r => r.GetAssignmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAssignment?)null);

        var handler = new ShiftHandlers.RemoveEmployeeFromShiftCommandHandler(_shiftRepoMock.Object);
        await handler.Handle(new RemoveEmployeeFromShiftCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        _shiftRepoMock.Verify(r => r.SaveAssignmentAsync(It.IsAny<ShiftAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ShiftValidators_Validate_Correctly()
    {
        var createValidator = new ShiftValidators.CreateShiftCommandValidator();
        var updateValidator = new ShiftValidators.UpdateShiftCommandValidator();
        var assignValidator = new ShiftValidators.AssignEmployeeToShiftCommandValidator();

        // Valid create
        var validCreate = new CreateShiftCommand(Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule);
        createValidator.TestValidate(validCreate).ShouldNotHaveAnyValidationErrors();

        // Invalid create with empty company & branch & invalid schedule
        var invalidCreate = new CreateShiftCommand(Guid.Empty, Guid.Empty, "", new ShiftSchedule { Type = ShiftScheduleType.DailySameTime });
        var createRes = createValidator.TestValidate(invalidCreate);
        createRes.ShouldHaveValidationErrorFor(x => x.CompanyId);
        createRes.ShouldHaveValidationErrorFor(x => x.BranchId);
        createRes.ShouldHaveValidationErrorFor(x => x.Name);
        createRes.ShouldHaveValidationErrorFor(x => x.Schedule);

        // Valid update
        var validUpdate = new UpdateShiftCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Shift", _validSchedule);
        updateValidator.TestValidate(validUpdate).ShouldNotHaveAnyValidationErrors();

        // Invalid update
        var invalidUpdate = new UpdateShiftCommand(Guid.Empty, Guid.Empty, Guid.Empty, "", new ShiftSchedule { Type = ShiftScheduleType.DailySameTime });
        var updateRes = updateValidator.TestValidate(invalidUpdate);
        updateRes.ShouldHaveValidationErrorFor(x => x.Id);
        updateRes.ShouldHaveValidationErrorFor(x => x.CompanyId);
        updateRes.ShouldHaveValidationErrorFor(x => x.BranchId);
        updateRes.ShouldHaveValidationErrorFor(x => x.Name);
        updateRes.ShouldHaveValidationErrorFor(x => x.Schedule);

        // Valid assign
        var validAssign = new AssignEmployeeToShiftCommand(Guid.NewGuid(), Guid.NewGuid());
        assignValidator.TestValidate(validAssign).ShouldNotHaveAnyValidationErrors();

        // Invalid assign
        var invalidAssign = new AssignEmployeeToShiftCommand(Guid.Empty, Guid.Empty);
        var assignRes = assignValidator.TestValidate(invalidAssign);
        assignRes.ShouldHaveValidationErrorFor(x => x.ShiftId);
        assignRes.ShouldHaveValidationErrorFor(x => x.EmployeeId);
    }
}
