using CleaningSuite.Domain.Common;
namespace CleaningSuite.Domain.QualityCycle;
public class QualityCycleReportShare : BaseDocument
{
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public string ReportJson { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public bool IsRevoked { get; set; }
    public string? RecipientEmail { get; set; }
    public DateTime? EmailSentUtc { get; set; }
    public string? EmailError { get; set; }
    public DateTime? AcknowledgedUtc { get; set; }
}
