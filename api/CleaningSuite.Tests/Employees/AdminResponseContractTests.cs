using System.Text.Json;
using CleaningSuite.Application.Access;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Invoicing;

namespace CleaningSuite.Tests.Employees;

public class AdminResponseContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void BookingAdminContractPreservesOperationalFinancialAndHistoryFieldsWithoutPersistenceVersion()
    {
        var booking = new Booking { Service = new() { NameFi="FI", NameEn="EN", NameSv="SV", PriceNet=123.4567m, VatRatePercent=25.5m },
            Customer = new() { Email="admin-visible-email" }, CustomerReference="admin-visible-reference", Notes="admin-visible-note",
            History=new() { new() { By="admin-visible-actor", Note="admin-visible-history" } } };
        var dto=AdminResponses.Booking(booking);
        Assert.Equal(123.4567m,dto.Service.PriceNet); Assert.Equal("EN",dto.Service.NameEn); Assert.Equal("SV",dto.Service.NameSv);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(dto,Json));
        Assert.Equal("admin-visible-email",json.RootElement.GetProperty("customer").GetProperty("email").GetString());
        Assert.Equal("admin-visible-reference",json.RootElement.GetProperty("customerReference").GetString());
        Assert.Equal("admin-visible-actor",json.RootElement.GetProperty("history")[0].GetProperty("by").GetString());
        Assert.False(json.RootElement.TryGetProperty("version",out _));
    }

    [Fact]
    public void InvoiceAdminContractPreservesIssuerBankFieldsDecimalLinesAndCalculatedTotals()
    {
        var invoice=new Invoice { Issuer=new() { BankAccountIBAN="admin-visible-iban",BankBic="admin-visible-bic" },
            Lines=new() { new() { Quantity=1.5m,UnitPriceNet=123.4567m,VatAmount=47.2222m,VatRatePercent=25.5m } },
            Total=new() { Net=185.18505m,Vat=47.2222m } };
        var dto=AdminResponses.Invoice(invoice);
        Assert.Equal(invoice.Lines[0].Total,Assert.Single(dto.Lines).Total);
        Assert.Equal(invoice.Total.Gross,dto.Total.Gross);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(dto,Json));
        Assert.Equal("admin-visible-iban",json.RootElement.GetProperty("issuer").GetProperty("bankAccountIBAN").GetString());
        Assert.Equal("admin-visible-bic",json.RootElement.GetProperty("issuer").GetProperty("bankBic").GetString());
        Assert.False(json.RootElement.TryGetProperty("version",out _));
        Assert.Equal(123.4567m,json.RootElement.GetProperty("lines")[0].GetProperty("unitPriceNet").GetDecimal());
    }
}
