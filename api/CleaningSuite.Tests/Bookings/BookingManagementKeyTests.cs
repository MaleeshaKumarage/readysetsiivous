using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Bookings.Commands;
using CleaningSuite.Application.Bookings.Queries;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Bookings;
using Moq;

namespace CleaningSuite.Tests.Bookings;

public class BookingManagementKeyTests
{
    [Fact]
    public void Legacy_json_booking_snapshot_defaults_to_eur_without_a_column_migration()
    {
        // MartenConfig uses Newtonsoft document serialization, not EF columns.
        var booking = Newtonsoft.Json.JsonConvert.DeserializeObject<Booking>("""{"Service":{"NameFi":"Siivous","PriceNet":50,"VatRatePercent":25.5}}""");
        Assert.Equal("EUR", booking!.Service.Currency);
        Assert.Equal(50m, booking.Service.PriceNet);
    }

    [Fact]
    public void Generated_keys_are_distinct_canonical_and_only_match_their_hash()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => BookingManagementKey.Generate()).ToArray();
        Assert.Equal(100, keys.Distinct().Count());
        foreach (var key in keys)
        {
            Assert.Matches("^[A-F0-9]{64}$", key);
            var hash = BookingManagementKey.Hash(key);
            Assert.NotEqual(key, hash);
            Assert.True(BookingManagementKey.Verify(key, hash));
            Assert.False(BookingManagementKey.Verify(key.ToLowerInvariant(), hash));
            Assert.False(BookingManagementKey.Verify(key, "invalid digest"));
        }
    }

    public static TheoryData<string?> InvalidKeys => new() { null, "", "123", "ABC123", new string('G', 64), new string('A', 63), new string('A', 65), new string('A', 100000), new string('a', 64), new string('Ａ', 64), new string('0', 64) };

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task Invalid_credentials_never_read_or_mutate_present_absent_or_legacy_bookings(string? key)
    {
        var correct = BookingManagementKey.Generate();
        foreach (var booking in new Booking?[] { null, new() { CustomerReference = "ABC123", Customer = new() { Phone = "123" } }, new() { ManagementKeyHash = BookingManagementKey.Hash(correct) } })
        {
            var repository = new Mock<IBookingRepository>();
            repository.Setup(x => x.GetByNumberAsync("number", default)).ReturnsAsync(booking);
            Assert.Null(await new GetPublicBookingHandler(repository.Object).Handle(new("number", key), default));
            var failure = await Assert.ThrowsAsync<NotFoundException>(() => new CancelPublicBookingHandler(repository.Object).Handle(new("number", key), default));
            if (!string.IsNullOrEmpty(key)) Assert.DoesNotContain(key, failure.Message);
            repository.Verify(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Never);
            if (booking is not null) Assert.Equal(Booking.StatusNew, booking.Status);
        }
    }

    [Fact]
    public async Task Correct_key_reads_limited_dto_and_cancels_eligible_past_booking()
    {
        var key = BookingManagementKey.Generate();
        var booking = new Booking { BookingNumber = "number", ManagementKeyHash = BookingManagementKey.Hash(key), StartUtc = DateTime.UtcNow.AddDays(-1), Customer = new() { Name = "Synthetic", Phone = "private" } };
        var repository = new Mock<IBookingRepository>();
        repository.Setup(x => x.GetByNumberAsync("number", default)).ReturnsAsync(booking);
        var dto = await new GetPublicBookingHandler(repository.Object).Handle(new("number", key), default);
        Assert.Equal("Synthetic", dto!.CustomerName);
        Assert.DoesNotContain(key, System.Text.Json.JsonSerializer.Serialize(dto));
        Assert.DoesNotContain(booking.ManagementKeyHash, System.Text.Json.JsonSerializer.Serialize(dto));
        await new CancelPublicBookingHandler(repository.Object).Handle(new("number", key), default);
        Assert.Equal(Booking.StatusCancelled, booking.Status);
        Assert.Equal("customer", Assert.Single(booking.History).By);
        repository.Verify(x => x.SaveAsync(booking, default), Times.Once);
    }

    [Theory]
    [InlineData(Booking.StatusCancelled)]
    [InlineData(Booking.StatusCompleted)]
    public async Task Correct_key_cannot_cancel_terminal_states(string status)
    {
        var key = BookingManagementKey.Generate();
        var repository = new Mock<IBookingRepository>();
        repository.Setup(x => x.GetByNumberAsync("number", default)).ReturnsAsync(new Booking { Status = status, ManagementKeyHash = BookingManagementKey.Hash(key) });
        await Assert.ThrowsAsync<SlotConflictException>(() => new CancelPublicBookingHandler(repository.Object).Handle(new("number", key), default));
        repository.Verify(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Another_booking_or_tenant_key_does_not_authorize_and_legacy_staff_access_remains()
    {
        var key = BookingManagementKey.Generate();
        var repository = new Mock<IBookingRepository>();
        var legacy = new Booking();
        repository.Setup(x => x.GetByNumberAsync("other", default)).ReturnsAsync(new Booking { ManagementKeyHash = BookingManagementKey.Hash(BookingManagementKey.Generate()) });
        repository.Setup(x => x.GetByIdAsync(legacy.Id, default)).ReturnsAsync(legacy);
        Assert.Null(await new GetPublicBookingHandler(repository.Object).Handle(new("other", key), default));
        Assert.Null(await new GetPublicBookingHandler(repository.Object).Handle(new("absent-in-tenant", key), default));
        Assert.Same(legacy, await new GetBookingDetailHandler(repository.Object).Handle(new(legacy.Id), default));
        await new ConfirmBookingHandler(repository.Object).Handle(new(legacy.Id, null), default);
        Assert.Equal(Booking.StatusConfirmed, legacy.Status);
    }

    [Fact]
    public void Credential_records_do_not_print_the_key()
    {
        var key = BookingManagementKey.Generate();
        Assert.DoesNotContain(key, new PublicBookingResult(Guid.NewGuid(), "number", "ref", key).ToString());
        Assert.DoesNotContain(key, new GetPublicBookingQuery("number", key).ToString());
        Assert.DoesNotContain(key, new CancelPublicBookingCommand("number", key).ToString());
        var booking = new Booking { ManagementKeyHash = BookingManagementKey.Hash(key) };
        foreach (var dto in new object[] { AdminResponses.Booking(booking), SelfResponses.Booking(booking) })
        {
            var json = System.Text.Json.JsonSerializer.Serialize(dto);
            Assert.DoesNotContain(key, json);
            Assert.DoesNotContain(booking.ManagementKeyHash, json);
            Assert.DoesNotContain("ManagementKey", json);
        }
    }
}
