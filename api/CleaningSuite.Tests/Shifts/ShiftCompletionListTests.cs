using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class ShiftCompletionListTests
{
    private static ShiftSchedule OneTime() => new()
    {
        Type = ShiftScheduleType.OneTime,
        OneTimeDate = new DateTime(2026, 11, 2),
        OneTimeStart = new TimeSpan(9, 0, 0),
        OneTimeEnd = new TimeSpan(11, 0, 0),
    };

    private static ShiftSchedule Weekly() => new()
    {
        Type = ShiftScheduleType.Weekly,
        WeeklyDay = DayOfWeek.Monday,
        WeeklyStart = new TimeSpan(9, 0, 0),
        WeeklyEnd = new TimeSpan(11, 0, 0),
    };

    [Fact]
    public async Task OneTimeShift_WithSubmittedChecklist_IsCompleted_AndOpenOnesAreNot()
    {
        var companyId = Guid.NewGuid();
        var done = Shift.Create(companyId, Guid.NewGuid(), "Done visit", OneTime(), null, null, null);
        var open = Shift.Create(companyId, Guid.NewGuid(), "Open visit", OneTime(), null, null, null);
        var recurring = Shift.Create(companyId, Guid.NewGuid(), "Weekly", Weekly(), null, null, null);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync([done, open, recurring]);
        var forms = new Mock<IQualityCycleRepository>();
        IReadOnlyCollection<Guid>? asked = null;
        forms.Setup(r => r.ListShiftIdsWithSubmittedFormAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>((ids, _) => asked = ids)
            .ReturnsAsync(new HashSet<Guid> { done.Id });

        var list = await new ShiftHandlers.ListShiftsQueryHandler(shifts.Object, forms.Object)
            .Handle(new ListShiftsQuery(null, null), default);

        Assert.True(list.Single(s => s.Id == done.Id).IsCompleted);
        Assert.False(list.Single(s => s.Id == open.Id).IsCompleted);
        Assert.False(list.Single(s => s.Id == recurring.Id).IsCompleted);
        // One batched lookup covering only the one-time shifts, and no per-shift form reads.
        forms.Verify(r => r.ListShiftIdsWithSubmittedFormAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { done.Id, open.Id }.Order(), asked!.Order());
        forms.Verify(r => r.ListFormsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecurringOnlyList_DoesNotQueryForms()
    {
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Weekly", Weekly(), null, null, null)]);
        var forms = new Mock<IQualityCycleRepository>(MockBehavior.Strict);

        var list = await new ShiftHandlers.ListShiftsQueryHandler(shifts.Object, forms.Object).Handle(new ListShiftsQuery(null, null), default);

        Assert.False(list.Single().IsCompleted);
    }

    [Fact]
    public void Api_container_resolves_the_list_handler_with_its_checklist_repository()
    {
        using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(x => x.ImplementationType == typeof(global::MartenSchemaInitializer)));
                services.Remove(services.Single(x => x.ImplementationType == typeof(CleaningSuite.Api.QualityCycleWorker)));
                services.Configure<Marten.StoreOptions>(opts => opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.None);
            }));
        using var scope = factory.Services.CreateScope();

        // The repository is a required constructor dependency, so this throws if it is ever unregistered.
        var handler = scope.ServiceProvider.GetRequiredService<MediatR.IRequestHandler<ListShiftsQuery, IReadOnlyList<ShiftDto>>>();

        Assert.IsType<ShiftHandlers.ListShiftsQueryHandler>(handler);
    }
}
