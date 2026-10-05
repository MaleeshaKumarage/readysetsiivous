namespace CleaningSuite.Application.QualityCycle;

public interface IQualityCyclePdfGenerator
{
    byte[] GenerateMonthlySummaryPdf(
        string shiftName,
        string companyName,
        string branchName,
        int year,
        int month,
        IReadOnlyList<QualityCycleFormDto> forms);
}
