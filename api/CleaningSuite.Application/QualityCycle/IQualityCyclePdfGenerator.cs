namespace CleaningSuite.Application.QualityCycle;

public interface IQualityCyclePdfGenerator
{
    byte[] GenerateMonthlyReportPdf(QualityCycleMonthlyReport report);
    byte[] GenerateMonthlySummaryPdf(
        string shiftName,
        string companyName,
        string branchName,
        int year,
        int month,
        IReadOnlyList<QualityCycleFormDto> forms,
        string language = "en");
}
