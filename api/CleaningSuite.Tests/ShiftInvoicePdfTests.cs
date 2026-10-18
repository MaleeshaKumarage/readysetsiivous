using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Infrastructure.Invoicing;
using UglyToad.PdfPig;
namespace CleaningSuite.Tests.Invoicing;
[Collection("PDF text verification")]
public class ShiftInvoicePdfTests
{
    [Theory]
    [InlineData("en", "Invoice", "Scheduled hours", "Amount due")]
    [InlineData("fi", "Lasku", "Suunnitellut tunnit", "Maksettavaa")]
    [InlineData("sv", "Faktura", "Planerade timmar", "Att betala")]
    public void Shift_invoice_translates_system_text_and_preserves_snapshots(string language, string title, string hours, string due)
    {
        var pdf = InvoicePdfRenderer.Render(Sample(language)); var text = Extract(pdf);
        foreach (var value in new[] { title, hours, due, "Methehe Oy", "Biweekly cleaning", "AAA1", "October adjustment agreed", "FI123456789", "RF123456", "01.10.2026", "31.10.2026", "10%" }) Assert.Contains(value, text);
        Assert.DoesNotContain("DO-NOT-SHOW-BOOKING", text); Assert.DoesNotContain('\0', text);
        if (language != "en") foreach (var phrase in new[] { "Scheduled hours", "Billing period", "Amount due", "Payment details", "Adjustment reason", "Fixed monthly rate", "Business ID" }) Assert.DoesNotContain(phrase, text);
        Save("shift-invoice-" + language, pdf);
    }
    [Fact]
    public void Booking_invoice_keeps_booking_reference_and_legacy_line_math()
    {
        var invoice = Sample("fi"); invoice.CompanyId = null; invoice.PeriodFrom = null; invoice.PeriodTo = null;
        invoice.Lines = [new() { Description = "Regular booking", Quantity = 2, UnitPriceNet = 25, VatRatePercent = 25.5m, VatAmount = 12.75m }];
        invoice.Total = new() { Net = 50, Vat = 12.75m };
        var text = Extract(InvoicePdfRenderer.Render(invoice));
        Assert.Contains("DO-NOT-SHOW-BOOKING", text); Assert.Contains("Regular booking", text); Assert.Contains("62,75", text);
    }
    [Fact]
    public void Long_addresses_descriptions_and_adjustment_reasons_paginate_without_losing_text()
    {
        var invoice = Sample("en"); invoice.BillingAddress.Street = string.Join(" ", Enumerable.Repeat("Long billing address", 30));
        invoice.Lines[0].Description = string.Join(" ", Enumerable.Repeat("A detailed cleaning service description", 150)) + " DESCRIPTION-END";
        invoice.Lines[0].OverrideReason = string.Join(" ", Enumerable.Repeat("Agreed billing adjustment", 100)) + " REASON-END";
        var pdf = InvoicePdfRenderer.Render(invoice); var text = Extract(pdf);
        using var doc = PdfDocument.Open(pdf); Assert.True(doc.NumberOfPages > 1);
        Assert.Contains("DESCRIPTION-END", text); Assert.Contains("REASON-END", text); Save("shift-invoice-long", pdf);
    }
    [Theory]
    [InlineData("en", "Sample issuer details")]
    [InlineData("fi", "Esimerkkitiedot laskuttajasta")]
    [InlineData("sv", "Exempeluppgifter för fakturautställaren")]
    public void Sample_issuer_is_labelled_without_labelling_real_issuers(string language, string label)
    {
        var invoice = Sample(language);
        Assert.DoesNotContain(label, Extract(InvoicePdfRenderer.Render(invoice)));
        invoice.IssuerIsSample = true;
        var pdf = InvoicePdfRenderer.Render(invoice);
        Assert.Contains(label, Extract(pdf));
        Save("sample-issuer-" + language, pdf);
    }
    private static Invoice Sample(string language) => new()
    {
        CompanyId = Guid.NewGuid(), Language = language, InvoiceNumber = "RSS-2026-001", BookingNumber = "DO-NOT-SHOW-BOOKING",
        IssueDate = new(2026, 11, 1), DueDate = new(2026, 11, 15), PeriodFrom = new(2026, 10, 1), PeriodTo = new(2026, 10, 31),
        Customer = new() { Name = "Methehe Oy", Email = "billing@example.test", Phone = "+358 000000" },
        CustomerBusinessId = "1234567-8", BillingAddress = new() { Street = "Example Street 4", PostalCode = "00100", City = "Helsinki", Country = "Finland" },
        Issuer = new() { CompanyName = "ReadySet Siivous", BusinessId = "7654321-0", Email = "issuer@example.test", Phone = "+358 111111", CompanyAddress = new() { Street = "Supplier Street 1", PostalCode = "00200", City = "Helsinki" }, BankAccountIBAN = "FI123456789", BankBic = "TESTFIHH" },
        PaymentReference = "RF123456", Total = new() { Net = 160, Vat = 39.25m },
        Lines = [new() { ShiftId = Guid.NewGuid(), ShiftName = "Biweekly cleaning", Description = "Biweekly cleaning", BranchName = "AAA1", Hours = 4, Occurrences = 2, PricingMode = "Monthly", PeriodFrom = new(2026, 10, 1), PeriodTo = new(2026, 10, 31), Quantity = 1, UnitPriceNet = 150, NetAmount = 150, VatRatePercent = 25.5m, VatAmount = 38.25m, OverrideReason = "October adjustment agreed" }, new() { Description = "Additional service", Quantity = 1, UnitPriceNet = 10, VatRatePercent = 10, VatAmount = 1 }]
    };
    private static string Extract(byte[] bytes) { using var doc = PdfDocument.Open(bytes); return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)); }
    private static void Save(string name, byte[] bytes) { var dir = Environment.GetEnvironmentVariable("INVOICE_PDF_QA_OUTPUT"); if (string.IsNullOrWhiteSpace(dir)) return; Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, name + ".pdf"), bytes); }
}
