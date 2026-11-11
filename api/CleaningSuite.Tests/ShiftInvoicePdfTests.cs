using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Infrastructure.Invoicing;
using UglyToad.PdfPig;
namespace CleaningSuite.Tests.Invoicing;
[Collection("PDF text verification")]
public class ShiftInvoicePdfTests
{
    [Fact]
    public async Task Manual_rows_retain_entered_order_after_shared_branch_grouping()
    {
        var f = new ManualInvoiceServiceTests.Fixture();
        var preview = await f.Service.PreviewAsync(f.Input with { Lines = Enumerable.Range(1, 12)
            .Select(i => new CleaningSuite.Application.Invoicing.ManualInvoiceRow($"Agreed row {i:D2}", 1, 50)).ToArray() }, default);
        var text = Extract(InvoicePdfRenderer.Render(preview.Invoice));
        Assert.True(text.IndexOf("Agreed row 09", StringComparison.Ordinal) < text.IndexOf("Agreed row 10", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Agreed row 10", StringComparison.Ordinal) < text.IndexOf("Agreed row 11", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("en")] [InlineData("fi")] [InlineData("sv")]
    public void Manual_invoice_uses_existing_layout_without_shift_or_booking_metadata(string language)
    {
        var invoice = Sample(language); invoice.PeriodFrom = null; invoice.PeriodTo = null;
        invoice.Lines = [new() { Description = "Separately agreed service", Quantity = 1.5m, UnitPriceNet = 100,
            NetAmount = 150, VatRatePercent = 25.5m, VatAmount = 38.25m }];
        invoice.Total = new() { Net = 150, Vat = 38.25m };
        var text = Extract(InvoicePdfRenderer.Render(invoice));
        Assert.Contains("Separately agreed service", text); Assert.Contains("Methehe Oy", text);
        Assert.Contains(language == "en" ? "188.25" : "188,25", text);
        Assert.DoesNotContain("DO-NOT-SHOW-BOOKING", text); Assert.DoesNotContain("Biweekly cleaning", text);
        Assert.DoesNotContain("Scheduled hours", text); Assert.DoesNotContain("Billing period", text);
    }

    [Fact]
    public void Manual_invoice_with_one_hundred_rows_keeps_final_description_on_multiple_pages()
    {
        var invoice = Sample("en"); invoice.PeriodFrom = null; invoice.PeriodTo = null;
        invoice.Lines = Enumerable.Range(1, 100).Select(i => new InvoiceLine { Description = $"Agreed material ROW-{i:D3}",
            Quantity = 2, UnitPriceNet = 50, NetAmount = 100, VatRatePercent = 25.5m, VatAmount = 25.5m }).ToList();
        invoice.Total = new() { Net = 10000, Vat = 2550 };
        var pdf = InvoicePdfRenderer.Render(invoice);
        using var doc = PdfDocument.Open(pdf); Assert.True(doc.NumberOfPages > 1);
        Assert.Contains("ROW-100", Extract(pdf));
    }
    [Theory]
    [InlineData("en", "Voided")] [InlineData("fi", "Mitätöity")] [InlineData("sv", "Makulerad")]
    public void Voided_invoice_pdf_clearly_retains_its_cancelled_status(string language, string status)
    {
        var invoice = Sample(language); invoice.Status = Invoice.StatusVoided;
        Assert.Contains(status, Extract(InvoicePdfRenderer.Render(invoice)));
    }
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
    [Theory]
    [InlineData("en", "Completed on")]
    [InlineData("fi", "Valmistumispäivä")]
    [InlineData("sv", "Slutförd den")]
    public void Completed_cleaning_rows_are_grouped_under_branches_without_program_labels(string language, string completedLabel)
    {
        var invoice = Sample(language); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        InvoiceLine Line(Guid branch, string name, string shift, int day) => new() { BranchId=branch, BranchName=name,
            ShiftId=Guid.NewGuid(), ShiftName=shift, PeriodFrom=new(2026,10,day), PeriodTo=new(2026,10,day),
            CompletedOn=new(2026,11,day), Quantity=1, UnitPriceNet=40, NetAmount=40, VatRatePercent=25.5m, VatAmount=10.2m };
        invoice.Lines = [Line(b,"BBB","VisitB",1),Line(a,"AAA","VisitA2",2),Line(a,"AAA","VisitA1",1)];
        invoice.Total = new() { Net=120, Vat=30.6m }; invoice.PeriodFrom=new(2026,11,1); invoice.PeriodTo=new(2026,11,30);
        var pdf = InvoicePdfRenderer.Render(invoice); var text = Extract(pdf);
        Assert.True(text.IndexOf("VisitA1", StringComparison.Ordinal) < text.IndexOf("VisitA2", StringComparison.Ordinal));
        Assert.True(text.IndexOf("VisitA2", StringComparison.Ordinal) < text.IndexOf("VisitB", StringComparison.Ordinal));
        Assert.Contains(completedLabel, text); Assert.DoesNotContain("partner",text,StringComparison.OrdinalIgnoreCase);
        Save("completed-cleaning-"+language,pdf);
    }
    [Theory]
    [InlineData("en", "Visit numbers: #3, #5")] [InlineData("fi", "Käyntinumerot: #3, #5")] [InlineData("sv", "Besöksnummer: #3, #5")]
    public void A_main_shift_line_names_the_visits_it_bills_and_other_lines_do_not(string language, string expected)
    {
        var invoice = Sample(language);
        invoice.Lines = [
            new() { ShiftId = Guid.NewGuid(), ShiftName = "Deep Cleaning - Testikatu 1", BranchName = "Apartment", PricingMode = "PerOccurrence", Quantity = 2, UnitPriceNet = 100,
                NetAmount = 200, VatRatePercent = 25.5m, VatAmount = 51, Occurrences = 2, PeriodFrom = new(2026, 10, 3), PeriodTo = new(2026, 10, 9), VisitNumbers = [3, 5] },
            new() { ShiftId = Guid.NewGuid(), ShiftName = "Weekly office clean", BranchName = "Office", PricingMode = "Hourly", Quantity = 4, UnitPriceNet = 30,
                NetAmount = 120, VatRatePercent = 25.5m, VatAmount = 30.6m, Occurrences = 2, PeriodFrom = new(2026, 10, 1), PeriodTo = new(2026, 10, 31) }];
        invoice.Total = new() { Net = 320, Vat = 81.6m };
        var text = Extract(InvoicePdfRenderer.Render(invoice));
        Assert.Contains(expected, text);
        // Only the main shift line carries visit numbers.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, "#\\d").Count);
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
