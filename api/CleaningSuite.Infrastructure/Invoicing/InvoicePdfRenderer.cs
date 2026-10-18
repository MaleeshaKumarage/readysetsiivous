using System.Globalization;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Infrastructure.QualityCycle;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CleaningSuite.Infrastructure.Invoicing;

/// <summary>Regenerates invoices solely from their immutable billing snapshots.</summary>
public static class InvoicePdfRenderer
{
    static InvoicePdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Render(Invoice invoice)
    {
        var lang = string.IsNullOrWhiteSpace(invoice.Language) ? "fi" : invoice.Language.ToLowerInvariant();
        if (lang is not ("en" or "fi" or "sv")) throw new ArgumentException("Unsupported invoice language.");
        var culture = CultureInfo.GetCultureInfo(lang switch { "fi" => "fi-FI", "sv" => "sv-SE", _ => "en-IE" });
        string T(string key) => Text(key, lang);
        string Amount(decimal value) => value.ToString("N2", culture) + " " + invoice.Currency;
        string Number(decimal value) => value.ToString("0.##", culture);
        string Period(DateOnly? from, DateOnly? to) => $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
        var barcode = FinnishBankBarcode.ForInvoice(invoice);
        return Document.Create(container => {
        container.Page(page =>
        {
            ReportPdfTheme.Page(page);
            page.Header().Element(c => ReportPdfTheme.Header(c, T("Invoice"), invoice.InvoiceNumber));
            page.Content().PaddingTop(14).Column(column =>
            {
                column.Spacing(12);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(issuer =>
                    {
                        issuer.Item().Text(T("Supplier")).Bold();
                        if (invoice.IssuerIsSample) issuer.Item().Text(T("Sample issuer details")).Bold();
                        issuer.Item().Text(invoice.Issuer.CompanyName).SemiBold();
                        issuer.Item().Text(T("Business ID") + ": " + invoice.Issuer.BusinessId);
                        Address(issuer, invoice.Issuer.CompanyAddress);
                        issuer.Item().Text(invoice.Issuer.Email);
                        issuer.Item().Text(invoice.Issuer.Phone);
                    });
                    row.RelativeItem().PaddingLeft(16).Column(meta =>
                    {
                        meta.Item().Text(T("Invoice number") + ": " + invoice.InvoiceNumber);
                        meta.Item().Text(T("Issue date") + $": {invoice.IssueDate:dd.MM.yyyy}");
                        meta.Item().Text(T("Status") + ": " + T(invoice.Status)).SemiBold();
                        meta.Item().Text(T("Due date") + $": {invoice.DueDate:dd.MM.yyyy}").Bold();
                        if (!string.IsNullOrWhiteSpace(invoice.PaymentReference)) meta.Item().Text(T("Payment reference") + ": " + invoice.PaymentReference);
                        if (invoice.PeriodFrom.HasValue) meta.Item().Text(T("Billing period") + ": " + Period(invoice.PeriodFrom, invoice.PeriodTo));
                        if (!invoice.CompanyId.HasValue && !string.IsNullOrWhiteSpace(invoice.BookingNumber)) meta.Item().Text(T("Booking reference") + ": " + invoice.BookingNumber);
                    });
                });
                column.Item().Background(ReportPdfTheme.Pale).Padding(10).Column(customer =>
                {
                    customer.Item().Text(T("Customer")).Bold();
                    customer.Item().Text(invoice.Customer.Name);
                    if (!string.IsNullOrWhiteSpace(invoice.CustomerBusinessId)) customer.Item().Text(T("Business ID") + ": " + invoice.CustomerBusinessId);
                    Address(customer, invoice.BillingAddress);
                    customer.Item().Text(invoice.Customer.Email);
                    customer.Item().Text(invoice.Customer.Phone);
                });
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols => { cols.RelativeColumn(4); cols.RelativeColumn(); cols.RelativeColumn(1.5f); cols.RelativeColumn(1.4f); cols.RelativeColumn(1.6f); });
                    table.Header(header =>
                    {
                        foreach (var heading in new[] { "Description", "Quantity", "Unit price", "VAT", "Total" }) header.Cell().Element(ReportPdfTheme.Head).Text(T(heading));
                    });
                    foreach (var line in invoice.Lines)
                    {
                        table.Cell().Element(Cell).Column(detail =>
                        {
                            detail.Item().Text(string.IsNullOrWhiteSpace(line.ShiftName) ? line.Description : line.ShiftName).SemiBold();
                            if (!string.IsNullOrWhiteSpace(line.ShiftName) && line.Description != line.ShiftName) detail.Item().Text(line.Description);
                            if (!string.IsNullOrWhiteSpace(line.BranchName)) detail.Item().Text(T("Branch") + ": " + line.BranchName);
                            if (line.ShiftId.HasValue)
                            {
                                detail.Item().Text(T("Scheduled hours") + ": " + Number(line.Hours));
                                detail.Item().Text(T("Occurrences") + ": " + line.Occurrences);
                                if (!string.IsNullOrWhiteSpace(line.PricingMode)) detail.Item().Text(T("Pricing") + ": " + T(line.PricingMode));
                                if (line.PeriodFrom.HasValue) detail.Item().Text(T("Billing period") + ": " + Period(line.PeriodFrom, line.PeriodTo));
                            }
                            detail.Item().Text(T("Net amount") + ": " + Amount(line.NetAmount ?? line.Quantity * line.UnitPriceNet));
                            if (!string.IsNullOrWhiteSpace(line.OverrideReason)) detail.Item().Text(T("Adjustment reason") + ": " + line.OverrideReason);
                        });
                        table.Cell().Element(Cell).Text(Number(line.Quantity));
                        table.Cell().Element(Cell).AlignRight().Text(Amount(line.UnitPriceNet));
                        table.Cell().Element(Cell).AlignRight().Text(Number(line.VatRatePercent) + "%\n" + Amount(line.VatAmount));
                        table.Cell().Element(Cell).AlignRight().Text(Amount(line.Total));
                    }
                });
                column.Item().EnsureSpace(110).AlignRight().Column(totals =>
                {
                    totals.Item().Text(T("Net amount") + ": " + Amount(invoice.Total.Net));
                    foreach (var group in invoice.Lines.GroupBy(x => x.VatRatePercent).OrderBy(x => x.Key))
                        totals.Item().Text(T("VAT") + " " + Number(group.Key) + "%: " + Amount(group.Sum(x => x.VatAmount)));
                    totals.Item().PaddingTop(5).Text(T("Amount due") + ": " + Amount(invoice.Total.Gross)).FontSize(15).Bold();
                });
                column.Item().EnsureSpace(65).Background(ReportPdfTheme.Pale).Padding(10).Column(payment =>
                {
                    payment.Item().Text(T("Payment details")).Bold();
                    payment.Item().Text("IBAN: " + invoice.Issuer.BankAccountIBAN);
                    payment.Item().Text("BIC: " + invoice.Issuer.BankBic);
                    if (!string.IsNullOrWhiteSpace(invoice.PaymentReference)) payment.Item().Text(T("Payment reference") + ": " + invoice.PaymentReference);
                    if (invoice.IssuerIsSample) payment.Item().Text(T("Sample payment notice")).Bold();
                });
            });
            page.Footer().Element(c => ReportPdfTheme.Footer(c, invoice.IssueDate, lang));
        });
        // The official transfer form is 210 × 101.6 mm: keep its physical dimensions
        // independent of the branded invoice's margins and overflowing line items.
        if (barcode is not null) container.Page(page =>
        {
            page.Size(QuestPDF.Helpers.PageSizes.A4); page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily(QuestPDF.Helpers.Fonts.Arial).FontSize(9)
                .FontColor("#000000").DisableFontFeature("liga").DisableFontFeature("clig").DisableFontFeature("dlig"));
            // Keep every payment field and its barcode together. Long legal names or
            // addresses may expand the form upwards, never split off the barcode.
            page.Content().AlignBottom().MinHeight(101.6f, Unit.Millimetre).ShowEntire().Column(form =>
            {
                form.Item().BorderTop(1.4f).PaddingHorizontal(12).PaddingVertical(4).Text(T("Credit transfer")).Bold();
                form.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        TransferField(left, T("Recipient account"), "IBAN " + Group(invoice.Issuer.BankAccountIBAN.Replace(" ", ""), 4, false) + "\nBIC " + invoice.Issuer.BankBic);
                        TransferField(left, T("Recipient"), invoice.Issuer.CompanyName + "\n" + invoice.Issuer.CompanyAddress.Street + "\n" + invoice.Issuer.CompanyAddress.PostalCode + " " + invoice.Issuer.CompanyAddress.City);
                        TransferField(left, T("Payer"), invoice.Customer.Name + "\n" + invoice.BillingAddress.Street + "\n" + invoice.BillingAddress.PostalCode + " " + invoice.BillingAddress.City);
                    });
                    row.RelativeItem().Column(right =>
                    {
                        right.Item().Padding(8).Text(T("Credit transfer terms")).FontSize(9);
                        TransferField(right, T("Payment reference"), Group(invoice.PaymentReference.Replace(" ", ""), 5, true));
                        right.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c => TransferField(c, T("Due date"), invoice.DueDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)));
                            r.RelativeItem().Column(c => TransferField(c, "EUR", invoice.Total.Gross.ToString("N2", culture)));
                        });
                    });
                });
                form.Item().Row(row =>
                {
                    row.RelativeItem().Column(c => TransferField(c, T("Signature"), " "));
                    row.RelativeItem().Column(c => TransferField(c, T("From account"), " "));
                });
                // The reserved barcode area contains no plaintext or branding.
                form.Item().PaddingTop(5, Unit.Millimetre).AlignCenter().Width(145, Unit.Millimetre)
                    .Height(17, Unit.Millimetre).Svg(FinnishBankBarcode.Svg(barcode));
            });
        });
        }).GeneratePdf();
    }

    private static void TransferField(ColumnDescriptor column, string label, string value) => column.Item()
        .Border(.37f).BorderColor("#000000").Padding(5).Column(c => { c.Item().Text(label); c.Item().Text(value); });
    private static string Group(string value, int size, bool fromRight)
    {
        var first = fromRight ? value.Length % size : size;
        if (first == 0) first = size;
        return value.Length <= first ? value : value[..first] + " " + string.Join(" ", Enumerable.Range(0, (value.Length - first + size - 1) / size).Select(i => value.Substring(first + i * size, Math.Min(size, value.Length - first - i * size))));
    }

    private static IContainer Cell(IContainer container) => container.BorderBottom(.5f).BorderColor(ReportPdfTheme.Line).Padding(6);
    private static void Address(ColumnDescriptor column, Address address)
    {
        column.Item().Text(address.Street);
        column.Item().Text($"{address.PostalCode} {address.City}".Trim());
        if (!string.IsNullOrWhiteSpace(address.Country)) column.Item().Text(address.Country);
    }
    private static string Text(string key, string language)
    {
        if (language == "en") return key switch {
            "Monthly" => "Fixed monthly rate", "Hourly" => "Hourly rate", "PerOccurrence" => "Per completed visit",
            "Sample payment notice" => "Sample details: enter real bank details before payment. No payment barcode is generated.",
            "Credit transfer terms" => "The payment will be cleared for the recipient in accordance with the general terms for payment transmission and only on the basis of the account number given by the payer.",
            _ => key };
        var pair = key switch
        {
            "Credit transfer" => ("Tilisiirto", "Girering"), "Recipient account" => ("Saajan tilinumero", "Mottagarens kontonummer"),
            "Recipient" => ("Saaja", "Mottagare"), "Payer" => ("Maksajan nimi ja osoite", "Betalarens namn och adress"),
            "Signature" => ("Allekirjoitus", "Underskrift"), "From account" => ("Tililtä nro", "Från konto nr"),
            "Sample payment notice" => ("Esimerkkitiedot: lisää oikeat pankkitiedot ennen maksamista. Maksuviivakoodia ei luoda.", "Exempeluppgifter: ange riktiga bankuppgifter före betalning. Ingen betalningsstreckkod skapas."),
            "Credit transfer terms" => ("Maksu välitetään saajalle maksujenvälityksen ehtojen mukaisesti ja vain maksajan ilmoittaman tilinumeron perusteella.", "Betalningen förmedlas till mottagaren enligt villkoren för betalningsförmedling och endast till det kontonummer som betalaren angivit."),
            "Status" => ("Tila", "Status"), "Issued" => ("Laadittu", "Utfärdad"),
            "Paid" => ("Maksettu", "Betald"), "Voided" => ("Mitätöity", "Makulerad"), "Draft" => ("Luonnos", "Utkast"),
            "Sample issuer details" => ("Esimerkkitiedot laskuttajasta", "Exempeluppgifter för fakturautställaren"),
            "Invoice" => ("Lasku", "Faktura"), "Supplier" => ("Myyjä", "Säljare"), "Business ID" => ("Y-tunnus", "FO-nummer"),
            "Invoice number" => ("Laskun numero", "Fakturanummer"), "Issue date" => ("Laskun päiväys", "Fakturadatum"),
            "Due date" => ("Eräpäivä", "Förfallodag"), "Payment reference" => ("Maksuviite", "Betalningsreferens"),
            "Billing period" => ("Laskutuskausi", "Faktureringsperiod"), "Booking reference" => ("Varausviite", "Bokningsreferens"),
            "Customer" => ("Asiakas", "Kund"), "Description" => ("Kuvaus", "Beskrivning"), "Quantity" => ("Määrä", "Antal"),
            "Unit price" => ("Veroton yksikköhinta", "Enhetspris exkl. moms"), "VAT" => ("ALV", "Moms"), "Total" => ("Yhteensä", "Totalt"),
            "Branch" => ("Toimipiste", "Verksamhetsställe"), "Scheduled hours" => ("Suunnitellut tunnit", "Planerade timmar"),
            "Occurrences" => ("Käynnit", "Besök"), "Pricing" => ("Hinnoittelu", "Prissättning"),
            "Adjustment reason" => ("Muutoksen peruste", "Orsak till justering"), "Net amount" => ("Veroton summa", "Belopp exkl. moms"),
            "Amount due" => ("Maksettavaa", "Att betala"), "Payment details" => ("Maksutiedot", "Betalningsuppgifter"),
            "Monthly" => ("Kiinteä kuukausihinta", "Fast månadspris"), "Hourly" => ("Tuntihinta", "Timpris"),
            "PerOccurrence" => ("Hinta toteutuneelta käynniltä", "Pris per genomfört besök"),
            _ => throw new ArgumentException("Missing invoice translation: " + key)
        };
        return language == "fi" ? pair.Item1 : pair.Item2;
    }
}
