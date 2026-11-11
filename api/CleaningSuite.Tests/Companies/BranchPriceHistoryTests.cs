using CleaningSuite.Application.Invoicing;
using CleaningSuite.Domain.Companies;
using FluentValidation;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace CleaningSuite.Tests.Companies;

// One price per branch, kept as dated versions. Shifts choose a version; partner bookings read the version in force today.
public class BranchPriceHistoryTests
{
    private static Branch NewBranch() => Branch.Create(Guid.NewGuid(), "Property");

    [Fact]
    public void A_later_date_adds_history_and_each_date_reads_the_version_in_force()
    {
        var branch = NewBranch();
        branch.SetPricing(100, 25.5m, 120, "Monthly", new DateOnly(2026, 1, 1));
        branch.SetPricing(120, 25.5m, 120, "Monthly", new DateOnly(2026, 6, 1));

        Assert.Equal(2, branch.Versions.Count);
        Assert.Equal(100m, branch.PriceOn(new DateOnly(2026, 5, 31))!.PriceNet);
        Assert.Equal(120m, branch.PriceOn(new DateOnly(2026, 6, 1))!.PriceNet);
        Assert.Null(branch.PriceOn(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void Correcting_the_price_for_an_existing_date_replaces_that_version()
    {
        var branch = NewBranch();
        branch.SetPricing(100, 25.5m, 120, "Monthly", new DateOnly(2026, 1, 1));
        branch.SetPricing(110, 25.5m, 120, "Monthly", new DateOnly(2026, 1, 1));

        var version = Assert.Single(branch.Versions);
        Assert.Equal(110m, version.PriceNet);
    }

    [Fact]
    public void A_pre_history_flat_price_reads_as_one_version_and_moves_into_history_on_the_next_change()
    {
        var branch = NewBranch();
        branch.PriceNet = 90; branch.VatRatePercent = 25.5m; branch.EstimatedDurationMinutes = 60;

        Assert.True(branch.PricingReady);
        Assert.Equal(90m, branch.CurrentPriceNet);
        var flat = Assert.Single(branch.Versions);
        Assert.Equal(new DateOnly(2026, 10, 1), flat.EffectiveFrom);

        branch.SetPricing(95, 25.5m, 60, "PerOccurrence", new DateOnly(2026, 11, 1));

        Assert.Equal(2, branch.Versions.Count);
        Assert.Null(branch.PriceNet);
        Assert.Equal(90m, branch.PriceOn(new DateOnly(2026, 10, 31))!.PriceNet);
        Assert.Equal(95m, branch.PriceOn(new DateOnly(2026, 11, 1))!.PriceNet);
    }

    [Fact]
    public void Re_saving_the_price_already_in_force_adds_no_history()
    {
        var branch = NewBranch();
        branch.PriceNet = 90; branch.VatRatePercent = 25.5m; branch.EstimatedDurationMinutes = 60;
        var before = branch.PricingVersion;

        branch.SetPricing(90, 25.5m, 60, "PerOccurrence", DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.Equal(before, branch.PricingVersion);
        Assert.Empty(branch.PriceVersions);
        Assert.Equal(90m, branch.CurrentPriceNet);
    }

    [Fact]
    public void Branch_documents_saved_before_price_history_still_load_their_price_and_keep_it_after_saving()
    {
        // Shape of a stored branch document from before PriceVersions existed (Marten uses Newtonsoft, camelCase).
        var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        var legacy = "{\"id\":\"" + Guid.NewGuid() + "\",\"companyId\":\"" + Guid.NewGuid() + "\",\"name\":\"Old\",\"isActive\":true,\"priceNet\":90,\"vatRatePercent\":25.5,\"estimatedDurationMinutes\":60,\"pricingVersion\":3}";
        var branch = JsonConvert.DeserializeObject<Branch>(legacy, settings)!;

        Assert.True(branch.PricingReady);
        Assert.Equal(90m, branch.CurrentPriceNet);

        // Saving it again moves the price into history and reads back the same from the new document.
        branch.SetPricing(95, 25.5m, 60, "PerOccurrence", new DateOnly(2026, 11, 1));
        var saved = JsonConvert.DeserializeObject<Branch>(JsonConvert.SerializeObject(branch, settings), settings)!;

        Assert.Equal(2, saved.Versions.Count);
        Assert.Equal(90m, saved.PriceOn(new DateOnly(2026, 10, 31))!.PriceNet);
        Assert.Equal(95m, saved.PriceOn(new DateOnly(2026, 11, 1))!.PriceNet);
        Assert.Null(saved.PriceNet);
    }

    [Fact]
    public void A_branch_with_only_future_prices_is_not_ready_for_partner_booking_today()
    {
        var branch = NewBranch();
        branch.SetPricing(100, 25.5m, 120, "PerOccurrence", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));

        Assert.False(branch.PricingReady);
        Assert.Null(branch.CurrentPriceNet);
    }

    [Fact]
    public void Creating_a_shift_without_a_choice_uses_the_only_version_or_the_version_in_force_today()
    {
        var none = NewBranch();
        Assert.Null(ShiftBillingSettingsService.ChooseBranchPrice(none, null, new DateOnly(2026, 10, 19)));
        Assert.Throws<ValidationException>(() =>
            ShiftBillingSettingsService.ChooseBranchPrice(none, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 19)));

        var single = NewBranch();
        single.SetPricing(100, 25.5m, 60, "Monthly", new DateOnly(2026, 1, 1));
        Assert.Equal(100m, ShiftBillingSettingsService.ChooseBranchPrice(single, null, new DateOnly(2026, 10, 19))!.PriceNet);

        var history = NewBranch();
        history.SetPricing(100, 25.5m, 60, "Monthly", new DateOnly(2026, 1, 1));
        history.SetPricing(120, 25.5m, 60, "Monthly", new DateOnly(2026, 9, 1));
        history.SetPricing(150, 25.5m, 60, "Monthly", new DateOnly(2027, 1, 1));

        // The default is the version in force today, not the newest one.
        Assert.Equal(new DateOnly(2026, 9, 1), ShiftBillingSettingsService.ChooseBranchPrice(history, null, new DateOnly(2026, 10, 19))!.EffectiveFrom);
        // An admin can pick an older version explicitly.
        Assert.Equal(100m, ShiftBillingSettingsService.ChooseBranchPrice(history, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 19))!.PriceNet);
        // A date that is not a version of this branch is rejected.
        Assert.Throws<ValidationException>(() =>
            ShiftBillingSettingsService.ChooseBranchPrice(history, new DateOnly(2026, 3, 3), new DateOnly(2026, 10, 19)));
    }

    [Fact]
    public void When_every_version_is_in_the_future_the_earliest_is_the_default()
    {
        var future = NewBranch();
        future.SetPricing(100, 25.5m, 60, "Monthly", new DateOnly(2027, 1, 1));
        future.SetPricing(120, 25.5m, 60, "Monthly", new DateOnly(2027, 6, 1));

        Assert.Equal(100m, ShiftBillingSettingsService.ChooseBranchPrice(future, null, new DateOnly(2026, 10, 19))!.PriceNet);
    }

    [Fact]
    public void The_old_flat_price_starts_on_01_10_2026_and_no_price_can_start_before_it()
    {
        var branch = NewBranch();
        branch.PriceNet = 90; branch.VatRatePercent = 25.5m; branch.EstimatedDurationMinutes = 60;

        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(branch.Versions).EffectiveFrom);
        Assert.Null(branch.PriceOn(new DateOnly(2026, 9, 30)));

        // Dated on or before 01/10/2026, the new price would collide with the flat price's start.
        Assert.Throws<ArgumentException>(() => branch.SetPricing(95, 25.5m, 60, "PerOccurrence", new DateOnly(2026, 10, 1)));
        Assert.Throws<ArgumentException>(() => branch.SetPricing(95, 25.5m, 60, "PerOccurrence", new DateOnly(2026, 9, 1)));
        Assert.Equal(90m, branch.PriceOn(new DateOnly(2026, 10, 1))!.PriceNet);
        Assert.Single(branch.Versions);
    }

    [Fact]
    public void Re_saving_the_old_flat_price_on_its_start_date_is_not_a_collision()
    {
        var branch = NewBranch();
        branch.PriceNet = 90; branch.VatRatePercent = 25.5m; branch.EstimatedDurationMinutes = 60;
        var before = branch.PricingVersion;

        branch.SetPricing(90, 25.5m, 60, "PerOccurrence", new DateOnly(2026, 10, 1));

        Assert.Equal(before, branch.PricingVersion);
        Assert.Single(branch.Versions);
    }

    [Fact]
    public void A_shift_billed_at_the_old_flat_price_keeps_resolving_after_a_later_price_is_added()
    {
        // Shifts store the flat price under 2000-01-01; that date must keep finding the same price.
        var branch = NewBranch();
        branch.PriceNet = 90; branch.VatRatePercent = 25.5m; branch.EstimatedDurationMinutes = 60;
        branch.SetPricing(95, 25.5m, 60, "PerOccurrence", new DateOnly(2026, 11, 1));

        var legacy = branch.VersionFrom(new DateOnly(2000, 1, 1));
        Assert.NotNull(legacy);
        Assert.Equal(90m, legacy!.PriceNet);
        Assert.Equal(new DateOnly(2026, 10, 1), legacy.EffectiveFrom);
        Assert.Equal(95m, branch.VersionFrom(new DateOnly(2026, 11, 1))!.PriceNet);
        Assert.Null(branch.VersionFrom(new DateOnly(2026, 6, 1)));
    }
}
