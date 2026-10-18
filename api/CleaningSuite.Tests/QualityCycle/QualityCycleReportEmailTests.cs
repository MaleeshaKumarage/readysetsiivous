using CleaningSuite.Application.QualityCycle;
namespace CleaningSuite.Tests.QualityCycle;
public class QualityCycleReportEmailTests
{
    private static QualityCycleMonthlyReport Report(string language = "en") => new(2026,10,"Europe/Helsinki",DateTime.UtcNow,[]) { Language=language };
    private static readonly DateTime Expiry = new(2026,11,17,12,0,0,DateTimeKind.Utc);
    private const string Link = "https://example.test/quality-report/?tenant=test&token=sample";
    [Theory]
    [InlineData("en","October 2026","Next steps")]
    [InlineData("fi","lokakuu 2026","Seuraavat vaiheet")]
    [InlineData("sv","oktober 2026","Nästa steg")]
    public void Legacy_month_and_all_email_labels_are_localized(string language,string month,string heading)
    {
        var email=QualityCycleReportEmail.Build(Report(language),Link,Expiry);
        Assert.Contains(month,email.Subject); Assert.Contains(System.Net.WebUtility.HtmlEncode(heading),email.Html);
        Assert.Contains("17.11.2026 14:00",email.Html);
        Assert.Contains("tenant=test&amp;token=sample",email.Html);
        if(language!="en") { Assert.DoesNotContain("Next steps",email.Html); Assert.DoesNotContain("What is included",email.Html); }
    }
    [Fact]
    public void Separate_months_are_not_presented_as_a_continuous_period()
    {
        var report=Report() with { Periods=[new(new(2026,8,1),new(2026,8,31)),new(new(2026,10,1),new(2026,10,31))] };
        var email=QualityCycleReportEmail.Build(report,Link,Expiry);
        Assert.Contains("August 2026; October 2026",email.Subject);
    }
    [Fact]
    public void Custom_dates_are_explicit_and_snapshot_names_are_html_encoded()
    {
        var row=new QualityCycleShiftReport(Guid.NewGuid(),"<script>shift</script>",Guid.NewGuid(),"A & B",Guid.NewGuid(),"Branch \"one\"",1,2,1,0,0,1,1,0,0,[],[],[]);
        var report=Report() with { Periods=[new(new(2026,10,2),new(2026,10,17))],Shifts=[row,row] };
        var email=QualityCycleReportEmail.Build(report,Link,Expiry);
        Assert.Contains("02.10.2026 – 17.10.2026",email.Subject);
        Assert.Contains("&lt;script&gt;shift&lt;/script&gt;",email.Html);
        Assert.Contains("A &amp; B",email.Html); Assert.DoesNotContain("<script>",email.Html);
        Assert.DoesNotContain(row.ShiftId.ToString(),email.Html);
        Assert.Equal(1,email.Html.Split("&lt;script&gt;shift&lt;/script&gt;").Length-1);
        Assert.Contains("not a signature",email.Html);
    }
}

