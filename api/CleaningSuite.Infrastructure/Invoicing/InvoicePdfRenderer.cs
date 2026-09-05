using CleaningSuite.Domain.Invoicing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CleaningSuite.Infrastructure.Invoicing;

/// <summary>
/// Renders an invoice PDF deterministically from the invoice document
/// snapshots (customer, issuer, lines) — zero storage, regenerable.
/// </summary>
public static class InvoicePdfRenderer
{
    static InvoicePdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Render(Invoice invoice)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(issuer =>
                        {
                            issuer.Item().Text(invoice.Issuer.CompanyName).FontSize(16).Bold();
                            issuer.Item().Text($"Y-tunnus {invoice.Issuer.BusinessId}");
                            issuer.Item().Text(invoice.Issuer.CompanyAddress.Street);
                            issuer.Item().Text(
                                $"{invoice.Issuer.CompanyAddress.PostalCode} {invoice.Issuer.CompanyAddress.City}");
                            issuer.Item().Text(invoice.Issuer.Email);
                        });

                        row.ConstantItem(150).Column(meta =>
                        {
                            meta.Item().Text("LASKU / INVOICE").FontSize(18).Bold();
                            meta.Item().Text($"Nro: {invoice.InvoiceNumber}");
                            meta.Item().Text($"Päiväys: {invoice.IssueDate:dd.MM.yyyy}");
                            meta.Item().Text($"Eräpäivä: {invoice.DueDate:dd.MM.yyyy}");
                            meta.Item().Text($"Viite: {invoice.BookingNumber}");
                        });
                    });
                });

                page.Content().Column(column =>
                {
                    column.Item().PaddingVertical(16).Column(customer =>
                    {
                        customer.Item().Text("Asiakas").Bold();
                        customer.Item().Text(invoice.Customer.Name);
                        customer.Item().Text(invoice.Customer.Email);
                        customer.Item().Text(invoice.Customer.Phone);
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);   // description
                            columns.RelativeColumn(1);   // qty
                            columns.RelativeColumn(2);   // unit price net
                            columns.RelativeColumn(2);   // vat
                            columns.RelativeColumn(2);   // total
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(Style).Text("Kuvaus");
                            header.Cell().Element(Style).Text("Määrä");
                            header.Cell().Element(Style).AlignRight().Text("Hinta (netto)");
                            header.Cell().Element(Style).AlignRight().Text("ALV");
                            header.Cell().Element(Style).AlignRight().Text("Yhteensä");
                        });

                        foreach (var line in invoice.Lines)
                        {
                            table.Cell().Text(line.Description);
                            table.Cell().Text(line.Quantity.ToString("0.##"));
                            table.Cell().AlignRight().Text($"{line.UnitPriceNet:F2} €");
                            table.Cell().AlignRight().Text($"{line.VatAmount:F2} €");
                            table.Cell().AlignRight().Text($"{line.Total:F2} €");
                        }
                    });

                    column.Item().PaddingTop(8).AlignRight().Column(totals =>
                    {
                        totals.Item().Text($"Veroton: {invoice.Total.Net:F2} €");
                        totals.Item().Text($"ALV ({invoice.Lines.FirstOrDefault()?.VatRatePercent ?? 0} %): {invoice.Total.Vat:F2} €");
                        totals.Item().Text($"YHTEENSÄ: {invoice.Total.Gross:F2} €").FontSize(14).Bold();
                    });
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().Text($"IBAN {invoice.Issuer.BankAccountIBAN}  BIC {invoice.Issuer.BankBic}").FontSize(8);
                    footer.Item().Text($"{invoice.Issuer.CompanyName} · {invoice.Issuer.Phone}").FontSize(8);
                });
            });
        }).GeneratePdf();
    }

    private static IContainer Style(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(4);
}
