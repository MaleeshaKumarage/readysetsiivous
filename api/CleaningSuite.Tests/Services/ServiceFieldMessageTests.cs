using System.Collections.Generic;
using CleaningSuite.Application.Services.Commands;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Services;

public class ServiceFieldMessageTests
{
    private static ServiceFields Fields(string slug = "deep-clean", int duration = 60, decimal price = 10m, decimal vat = 25.5m) => new(
        Slug: slug, Category: "cleaning",
        Name: new Dictionary<string, string> { ["fi"] = "Nimi" },
        Description: new Dictionary<string, string> { ["fi"] = "Kuvaus" },
        AdditionalInfo: null, DurationMinutes: duration, PriceNet: price, VatRatePercent: vat, Currency: "EUR",
        IsFeatured: false, SortOrder: 0, Icon: null, ImageUrl: null);

    [Fact]
    public void Negative_price_says_to_enter_zero_or_more()
    {
        new ServiceFieldsValidator().TestValidate(Fields(price: -1m))
            .ShouldHaveValidationErrorFor("PriceNet")
            .WithErrorMessage("Price cannot be negative. Enter 0 or a positive amount.");
    }

    [Fact]
    public void Duration_outside_range_says_the_allowed_range()
    {
        new ServiceFieldsValidator().TestValidate(Fields(duration: 0))
            .ShouldHaveValidationErrorFor("DurationMinutes")
            .WithErrorMessage("Choose a duration from 15 to 1440 minutes.");
    }

    [Fact]
    public void Bad_slug_shows_an_example()
    {
        new ServiceFieldsValidator().TestValidate(Fields(slug: "Deep Clean!"))
            .ShouldHaveValidationErrorFor("Slug")
            .WithErrorMessage("Slug can use lowercase letters, numbers and dashes, 2 to 61 characters, such as deep-clean.");
    }

    [Fact]
    public void Vat_outside_range_says_what_to_choose()
    {
        new ServiceFieldsValidator().TestValidate(Fields(vat: 80m))
            .ShouldHaveValidationErrorFor("VatRatePercent")
            .WithErrorMessage("VAT must be between 0% and 50%. Choose the rate that applies to this service.");
    }
}
