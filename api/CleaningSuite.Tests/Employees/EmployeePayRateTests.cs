using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.PayRates;
using CleaningSuite.Domain.Employees;
using FluentValidation;
using NSubstitute;

namespace CleaningSuite.Tests.Employees;

public class EmployeePayRateRulesTests
{
    private static readonly Guid Employee = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static EmployeePayRate Rate(string from, decimal rate, string source = EmployeePayRate.SourceManual) =>
        new() { EmployeeId = Employee, EffectiveFrom = DateOnly.Parse(from), HourlyRate = rate, Source = source };

    [Fact]
    public void No_entries_means_no_rate_for_any_date()
    {
        Assert.Null(EmployeePayRateRules.EffectiveOn([], new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Rate_applies_from_its_start_date_inclusive_and_switches_on_the_next_entry()
    {
        var entries = new[] { Rate("2026-01-01", 18m), Rate("2026-12-01", 21m) };
        Assert.Null(EmployeePayRateRules.EffectiveOn([Rate("2026-12-01", 21m)], new DateOnly(2026, 11, 30)));
        Assert.Equal(18m, EmployeePayRateRules.EffectiveOn(entries, new DateOnly(2026, 11, 30))!.HourlyRate);
        Assert.Equal(21m, EmployeePayRateRules.EffectiveOn(entries, new DateOnly(2026, 12, 1))!.HourlyRate);
        Assert.Equal(21m, EmployeePayRateRules.EffectiveOn(entries, new DateOnly(2027, 3, 1))!.HourlyRate);
    }

    [Fact]
    public void Latest_applicable_entry_wins_regardless_of_insertion_order()
    {
        var entries = new[] { Rate("2026-12-01", 21m), Rate("2026-01-01", 18m), Rate("2026-06-01", 19m) };
        Assert.Equal(19m, EmployeePayRateRules.EffectiveOn(entries, new DateOnly(2026, 7, 15))!.HourlyRate);
    }

    [Theory]
    [InlineData(-0.01, "negative")]
    [InlineData(10000.01, "10000")]
    [InlineData(18.005, "two decimal")]
    public void Invalid_rates_are_refused_with_a_reason(double rate, string reasonPart)
    {
        var refusal = EmployeePayRateRules.ValidateRate((decimal)rate);
        Assert.NotNull(refusal);
        Assert.Contains(reasonPart, refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(18.5)]
    [InlineData(10000)]
    public void Valid_rates_are_accepted(double rate)
    {
        Assert.Null(EmployeePayRateRules.ValidateRate((decimal)rate));
    }

    [Fact]
    public void Identity_is_stable_per_employee_and_date_and_differs_otherwise()
    {
        var first = EmployeePayRateRules.IdFor(Employee, new DateOnly(2026, 12, 1));
        Assert.Equal(first, EmployeePayRateRules.IdFor(Employee, new DateOnly(2026, 12, 1)));
        Assert.NotEqual(first, EmployeePayRateRules.IdFor(Employee, new DateOnly(2026, 12, 2)));
        Assert.NotEqual(first, EmployeePayRateRules.IdFor(Guid.NewGuid(), new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Legacy_rate_is_confirmable_once_and_only_before_existing_dated_rates()
    {
        Assert.Null(EmployeePayRateRules.LegacyConfirmationRefusal([], new DateOnly(2026, 1, 1)));
        var manual = new[] { Rate("2026-06-01", 19m) };
        Assert.Null(EmployeePayRateRules.LegacyConfirmationRefusal(manual, new DateOnly(2026, 1, 1)));
        Assert.NotNull(EmployeePayRateRules.LegacyConfirmationRefusal(manual, new DateOnly(2026, 6, 1)));
        Assert.NotNull(EmployeePayRateRules.LegacyConfirmationRefusal(manual, new DateOnly(2026, 7, 1)));
        var confirmed = new[] { Rate("2026-01-01", 18m, EmployeePayRate.SourceLegacyConfirmed) };
        Assert.NotNull(EmployeePayRateRules.LegacyConfirmationRefusal(confirmed, new DateOnly(2025, 1, 1)));
    }
}

public class EmployeePayRateHandlerTests
{
    private static readonly Guid EmployeeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakePayRates : IEmployeePayRateRepository
    {
        public readonly Dictionary<Guid, EmployeePayRate> Stored = new();

        public Task<IReadOnlyList<EmployeePayRate>> ListAsync(Guid employeeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmployeePayRate>>(Stored.Values.Where(e => e.EmployeeId == employeeId).ToList());

        public Task AddAsync(EmployeePayRate entry, CancellationToken ct = default)
        {
            if (!Stored.TryAdd(entry.Id, entry))
                throw new EmployeePayRateConflictException("A pay rate already starts on this date. Choose another date.");
            return Task.CompletedTask;
        }
    }

    private static (IEmployeeRepository employees, FakePayRates rates) Setup(decimal? legacy)
    {
        var employees = Substitute.For<IEmployeeRepository>();
        employees.GetByIdAsync(EmployeeId, Arg.Any<CancellationToken>())
            .Returns(new Employee { Id = EmployeeId, PayRate = legacy });
        employees.GetByIdAsync(Arg.Is<Guid>(id => id != EmployeeId), Arg.Any<CancellationToken>())
            .Returns((Employee?)null);
        return (employees, new FakePayRates());
    }

    [Fact]
    public async Task Adding_a_dated_rate_stores_it_and_returns_the_profile()
    {
        var (employees, rates) = Setup(legacy: 18m);
        var handler = new AddEmployeePayRateHandler(employees, rates);
        var profile = await handler.Handle(new AddEmployeePayRateCommand(EmployeeId, 21m, new DateOnly(2027, 1, 1), "admin-sub"), default);

        var entry = Assert.Single(profile.Rates);
        Assert.Equal(21m, entry.HourlyRate);
        Assert.Equal(new DateOnly(2027, 1, 1), entry.EffectiveFrom);
        Assert.Equal(EmployeePayRate.SourceManual, entry.Source);
        Assert.Equal(18m, profile.Legacy.Amount);
        Assert.False(profile.Legacy.Confirmed);
        Assert.Equal("admin-sub", rates.Stored.Values.Single().RecordedBySubject);
    }

    [Fact]
    public async Task A_second_rate_on_the_same_date_is_a_conflict_and_nothing_changes()
    {
        var (employees, rates) = Setup(legacy: null);
        var handler = new AddEmployeePayRateHandler(employees, rates);
        await handler.Handle(new AddEmployeePayRateCommand(EmployeeId, 21m, new DateOnly(2027, 1, 1), "admin-sub"), default);

        await Assert.ThrowsAsync<EmployeePayRateConflictException>(() =>
            handler.Handle(new AddEmployeePayRateCommand(EmployeeId, 25m, new DateOnly(2027, 1, 1), "admin-sub"), default));
        Assert.Equal(21m, Assert.Single(rates.Stored.Values).HourlyRate);
    }

    [Fact]
    public async Task Invalid_rate_is_refused_before_anything_is_stored()
    {
        var (employees, rates) = Setup(legacy: null);
        var handler = new AddEmployeePayRateHandler(employees, rates);
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new AddEmployeePayRateCommand(EmployeeId, -1m, new DateOnly(2027, 1, 1), "admin-sub"), default));
        Assert.Empty(rates.Stored);
    }

    [Fact]
    public async Task Unknown_employee_is_not_found()
    {
        var (employees, rates) = Setup(legacy: null);
        var handler = new AddEmployeePayRateHandler(employees, rates);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new AddEmployeePayRateCommand(Guid.NewGuid(), 21m, new DateOnly(2027, 1, 1), "admin-sub"), default));
    }

    [Fact]
    public async Task Confirming_the_legacy_rate_imports_the_existing_amount_once()
    {
        var (employees, rates) = Setup(legacy: 18.5m);
        var handler = new ConfirmLegacyPayRateHandler(employees, rates);
        var profile = await handler.Handle(new ConfirmLegacyPayRateCommand(EmployeeId, new DateOnly(2026, 1, 1), "admin-sub"), default);

        var entry = Assert.Single(profile.Rates);
        Assert.Equal(18.5m, entry.HourlyRate);
        Assert.Equal(EmployeePayRate.SourceLegacyConfirmed, entry.Source);
        Assert.True(profile.Legacy.Confirmed);

        await Assert.ThrowsAsync<EmployeePayRateConflictException>(() =>
            handler.Handle(new ConfirmLegacyPayRateCommand(EmployeeId, new DateOnly(2025, 1, 1), "admin-sub"), default));
        Assert.Single(rates.Stored);
    }

    [Fact]
    public async Task Confirming_without_a_legacy_rate_is_refused_and_invents_nothing()
    {
        var (employees, rates) = Setup(legacy: null);
        var handler = new ConfirmLegacyPayRateHandler(employees, rates);
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new ConfirmLegacyPayRateCommand(EmployeeId, new DateOnly(2026, 1, 1), "admin-sub"), default));
        Assert.Empty(rates.Stored);
    }

    [Fact]
    public async Task Profile_shows_no_rate_for_a_date_before_history_starts()
    {
        var (employees, rates) = Setup(legacy: null);
        await new AddEmployeePayRateHandler(employees, rates)
            .Handle(new AddEmployeePayRateCommand(EmployeeId, 21m, new DateOnly(2027, 1, 1), "admin-sub"), default);
        var entries = await rates.ListAsync(EmployeeId);
        Assert.Null(EmployeePayRateRules.EffectiveOn(entries, new DateOnly(2026, 12, 31)));
        Assert.Equal(21m, EmployeePayRateRules.EffectiveOn(entries, new DateOnly(2027, 1, 1))!.HourlyRate);
    }
}
