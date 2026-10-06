using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Api.Controllers;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftsAdminControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly ShiftsAdminController _controller;

    public ShiftsAdminControllerTests()
    {
        _controller = new ShiftsAdminController(_mediatorMock.Object);
    }

    private static ShiftDto DummyShiftDto(Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Morning Shift",
        new ShiftSchedule
        {
            Type = ShiftScheduleType.DailySameTime,
            DailyStart = new TimeSpan(8, 0, 0),
            DailyEnd = new TimeSpan(16, 0, 0)
        },
        "Notes",
        true,
        null,
        null);

    [Fact]
    public async Task List_ReturnsOkResult_WithShifts()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var expected = new List<ShiftDto> { DummyShiftDto() };

        _mediatorMock.Setup(m => m.Send(It.Is<ListShiftsQuery>(q => q.CompanyId == companyId && q.BranchId == branchId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.List(companyId, branchId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expected, okResult.Value);
    }

    [Fact]
    public async Task Get_ReturnsOkResult_WithShift()
    {
        var shiftId = Guid.NewGuid();
        var expected = DummyShiftDto(shiftId);

        _mediatorMock.Setup(m => m.Send(It.Is<GetShiftQuery>(q => q.Id == shiftId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.Get(shiftId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expected, okResult.Value);
    }

    [Fact]
    public async Task Create_ReturnsOkResult_WithCreatedShift()
    {
        var expected = DummyShiftDto();
        var command = new CreateShiftCommand(expected.CompanyId, expected.BranchId, expected.Name, expected.Schedule);

        _mediatorMock.Setup(m => m.Send(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.Create(command, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expected, okResult.Value);
    }

    [Fact]
    public async Task Update_ReturnsOkResult_WithUpdatedShift()
    {
        var shiftId = Guid.NewGuid();
        var expected = DummyShiftDto(shiftId);
        var command = new UpdateShiftCommand(Guid.Empty, expected.CompanyId, expected.BranchId, expected.Name, expected.Schedule);

        _mediatorMock.Setup(m => m.Send(It.Is<UpdateShiftCommand>(c => c.Id == shiftId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.Update(shiftId, command, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expected, okResult.Value);
    }

    [Fact]
    public async Task Deactivate_ReturnsNoContentResult()
    {
        var shiftId = Guid.NewGuid();

        _mediatorMock.Setup(m => m.Send(It.Is<DeactivateShiftCommand>(c => c.Id == shiftId), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Deactivate(shiftId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Occurrences_ReturnsOkResult_WithOccurrences()
    {
        var shiftId = Guid.NewGuid();
        var from = DateTime.UtcNow;
        var to = from.AddDays(2);
        var expected = new List<ShiftOccurrenceDto> { new(from, to) };

        _mediatorMock.Setup(m => m.Send(It.Is<GetShiftOccurrencesQuery>(q => q.ShiftId == shiftId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.Occurrences(shiftId, from, to, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expected, okResult.Value);
    }

    [Fact]
    public async Task AssignEmployee_ReturnsOkResult_WithAssignment()
    {
        var shiftId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var command = new AssignEmployeeToShiftCommand(Guid.Empty, empId, "Note");
        var expected = new ShiftAssignmentDto(Guid.NewGuid(), shiftId, empId, DateTime.UtcNow, true, "Note");

        _mediatorMock.Setup(m => m.Send(It.Is<AssignEmployeeToShiftCommand>(c => c.ShiftId == shiftId && c.EmployeeId == empId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.AssignEmployee(shiftId, command, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expected, okResult.Value);
    }

    [Fact]
    public async Task RemoveEmployee_ReturnsNoContentResult()
    {
        var shiftId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        _mediatorMock.Setup(m => m.Send(It.Is<RemoveEmployeeFromShiftCommand>(c => c.ShiftId == shiftId && c.EmployeeId == empId), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.RemoveEmployee(shiftId, empId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
