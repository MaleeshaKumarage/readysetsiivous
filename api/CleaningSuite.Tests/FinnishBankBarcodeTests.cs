using System.Globalization;
using System.Xml.Linq;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Infrastructure.Invoicing;
using UglyToad.PdfPig;
namespace CleaningSuite.Tests.Invoicing;
[Collection("PDF text verification")]
public class FinnishBankBarcodeTests
{
    // Finance Finland Bank Bar Code Guide 5.3 §13.1 official v4 test invoices.
    [Theory]
    [InlineData("FI79 4405 2020 0360 82","868516259619897",4883.15,"2010-06-12","479440520200360820048831500000000868516259619897100612",40)]
    [InlineData("FI5810171000000122","559582243294671",482.99,"2012-01-31","458101710000001220004829900000000559582243294671120131",55)]
    [InlineData("FI0250004640001302","69875672083435364",693.80,"2011-07-24","402500046400013020006938000000069875672083435364110724",14)]
    [InlineData("FI1566010001530641","7758474790647489",7444.54,"2019-12-19","415660100015306410074445400000007758474790647489191219",63)]
    [InlineData("FI8333010001100775","92125374252539897737",150000.20,"2016-05-25","483330100011007751500002000092125374252539897737160525",45)]
    [InlineData("FI3936363002092492","590738390",1.03,"2023-03-11","439363630020924920000010300000000000000590738390230311",9)]
    [InlineData("FI9239390001003391","1357914",0.02,"2099-12-24","492393900010033910000000200000000000000001357914991224",30)]
    public void Official_vectors_have_exact_payload_and_set_c_checksum(string iban,string reference,decimal amount,string due,string expected,int checksum)
    {
        var payload=FinnishBankBarcode.CreatePayload(iban,reference,amount,DateTime.Parse(due,CultureInfo.InvariantCulture));
        Assert.Equal(expected,payload);var words=FinnishBankBarcode.Codewords(payload!);
        Assert.Equal(30,words.Count);Assert.Equal(105,words[0]);Assert.Equal(checksum,words[^2]);Assert.Equal(106,words[^1]);
        Assert.All(words.Skip(1).Take(27),w=>Assert.InRange(w,0,99));
    }
    [Theory]
    [InlineData("FI2112345600000784","12344",10,"2026-10-31")]
    [InlineData("DE89370400440532013000","12344",10,"2026-10-31")]
    [InlineData("FI2112345600000785","RF123456",10,"2026-10-31")]
    [InlineData("FI2112345600000785","12345",10,"2026-10-31")]
    [InlineData("FI2112345600000785","12344",0,"2026-10-31")]
    [InlineData("FI2112345600000785","12344",-1,"2026-10-31")]
    [InlineData("FI2112345600000785","12344",1000000,"2026-10-31")]
    [InlineData("FI2112345600000785","12344",1.001,"2026-10-31")]
    [InlineData("FI2112345600000785","12344",1,"2100-01-01")]
    [InlineData("FI2112345600000785","12344",1,"1999-12-31")]
    public void Unsupported_or_invalid_data_has_no_barcode(string iban,string reference,decimal amount,string due) => Assert.Null(FinnishBankBarcode.CreatePayload(iban,reference,amount,DateTime.Parse(due,CultureInfo.InvariantCulture)));
    [Fact]
    public void Svg_has_exact_physical_dimensions_quiet_zones_and_no_text()
    {
        var payload=FinnishBankBarcode.CreatePayload("FI2112345600000785","12344",199.25m,new(2026,10,31))!;
        var doc=XDocument.Parse(FinnishBankBarcode.Svg(payload));var root=doc.Root!;
        Assert.Equal("145mm",root.Attribute("width")!.Value);Assert.Equal("17mm",root.Attribute("height")!.Value);
        var bars=root.Elements().Where(e=>e.Attribute("fill")?.Value=="black").ToArray();
        decimal Value(XElement e,string a)=>decimal.Parse(e.Attribute(a)!.Value,CultureInfo.InvariantCulture);
        Assert.Equal(20,Value(bars.First(),"x"));Assert.InRange(Value(bars.Last(),"x")+Value(bars.Last(),"width"),124.99999m,125.00001m);
        Assert.All(bars,b=>{Assert.Equal(0,Value(b,"y"));Assert.Equal(12,Value(b,"height"));});Assert.DoesNotContain(root.Descendants(),e=>e.Name.LocalName=="text");
        var dir=Environment.GetEnvironmentVariable("INVOICE_PDF_QA_OUTPUT");if(!string.IsNullOrWhiteSpace(dir)){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"bank-barcode.svg"),doc.ToString());}
    }
    [Theory][InlineData("Draft",false)][InlineData("Paid",false)][InlineData("Voided",false)][InlineData("Issued",true)]
    public void Only_real_issued_invoices_can_have_scannable_payment_data(string status,bool sample)
    {
        var invoice=Invoice();invoice.Status=status;invoice.IssuerIsSample=sample;Assert.Null(FinnishBankBarcode.ForInvoice(invoice));
    }
    [Fact]
    public void Non_euro_invoices_are_not_encoded_as_euro_payments()
    {
        var invoice=Invoice();invoice.Currency="SEK";Assert.Null(FinnishBankBarcode.ForInvoice(invoice));
    }
    [Fact]
    public void Long_payment_names_and_addresses_stay_on_one_transfer_page_with_barcode()
    {
        var invoice=Invoice();invoice.Issuer.CompanyName=new string('a',200);
        invoice.Issuer.CompanyAddress.Street=string.Join(" ",Enumerable.Repeat("Street",40));
        invoice.Customer.Name=new string('b',200);invoice.BillingAddress.Street=string.Join(" ",Enumerable.Repeat("Address",30));
        using var pdf=PdfDocument.Open(InvoicePdfRenderer.Render(invoice));Assert.Equal(2,pdf.NumberOfPages);
        var text=string.Join(" ",pdf.GetPage(2).GetWords().Select(w=>w.Text));
        Assert.Contains("Allekirjoitus",text);Assert.Contains("Tililtä nro",text);Assert.Contains("31.10.2026",text);
        Assert.Contains("Street",text);Assert.Contains("Address",text);
    }
    [Theory][InlineData("en","Credit transfer","Sample details")][InlineData("fi","Tilisiirto","Esimerkkitiedot")][InlineData("sv","Girering","Exempeluppgifter")]
    public void Pdf_has_localized_payment_form_only_for_eligible_real_invoice(string language,string heading,string sampleText)
    {
        var invoice=Invoice();invoice.Language=language;var bytes=InvoicePdfRenderer.Render(invoice);
        using(var doc=PdfDocument.Open(bytes)){Assert.Equal(2,doc.NumberOfPages);Assert.Contains(heading,string.Join(" ",doc.GetPage(2).GetWords().Select(w=>w.Text)));}
        var dir=Environment.GetEnvironmentVariable("INVOICE_PDF_QA_OUTPUT");if(!string.IsNullOrWhiteSpace(dir)){Directory.CreateDirectory(dir);File.WriteAllBytes(Path.Combine(dir,"bank-transfer-"+language+".pdf"),bytes);}
        invoice.IssuerIsSample=true;using var sample=PdfDocument.Open(InvoicePdfRenderer.Render(invoice));Assert.Equal(1,sample.NumberOfPages);Assert.Contains(sampleText,string.Join(" ",sample.GetPage(1).GetWords().Select(w=>w.Text)));
    }
    private static Invoice Invoice()=>new(){Status="Issued",Language="fi",InvoiceNumber="2026-0001",IssueDate=new(2026,10,17),DueDate=new(2026,10,31),PaymentReference="12344",Customer=new(){Name="Customer Oy"},Issuer=new(){CompanyName="ReadySet Siivous",BankAccountIBAN="FI2112345600000785",BankBic="NDEAFIHH",CompanyAddress=new(){Street="Example Street 1",PostalCode="00100",City="Helsinki"}},BillingAddress=new(){Street="Customer Street 2",PostalCode="00100",City="Helsinki"},Lines=[new(){Description="Cleaning",Quantity=1,UnitPriceNet=150,VatAmount=38.25m,VatRatePercent=25.5m}],Total=new(){Net=150,Vat=38.25m}};
}
