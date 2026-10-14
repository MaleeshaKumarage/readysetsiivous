using System.Text.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using MediatR;

namespace CleaningSuite.Application.QualityCycle;
public record CreateQualityCycleReportShareCommand(int Year, int Month, Guid? CompanyId = null, Guid? BranchId = null, Guid? ShiftId = null) : IRequest<QualityCycleReportShareDto>;
public record QualityCycleReportShareDto(Guid Id, string Token, string TenantId, DateTime ExpiresUtc, bool IsRevoked, int Year, int Month);
public record RevokeQualityCycleReportShareCommand(Guid Id) : IRequest;
public record ListQualityCycleReportSharesQuery : IRequest<IReadOnlyList<QualityCycleReportShareDto>>;
public record GetSharedQualityCycleReportPdfQuery(string Token) : IRequest<byte[]>;
public class CreateQualityCycleReportShareHandler(QualityCycleMonthlyReportService reports, IQualityCycleRepository repository, ITenantContext tenant, TimeProvider clock)
    : IRequestHandler<CreateQualityCycleReportShareCommand, QualityCycleReportShareDto>
{
    public async Task<QualityCycleReportShareDto> Handle(CreateQualityCycleReportShareCommand request, CancellationToken ct) {
        if (!request.ShiftId.HasValue && !request.BranchId.HasValue && !request.CompanyId.HasValue)
            throw new QualityCycleConflictException("Select a company, branch or shift before sharing a report.");
        var report = await reports.BuildAsync(new(request.Year, request.Month, request.CompanyId, request.BranchId, request.ShiftId), ct);
        // Freeze summary and approved submitted evidence, excluding full forms and
        // their employee identities/form tokens. Existing snapshots remain unchanged.
        report = report with { Shifts = report.Shifts.Select(s => s with {
            Forms = Array.Empty<QualityCycleFormDto>()
        }).ToList() };
        var share = new QualityCycleReportShare { CreatedUtc = clock.GetUtcNow().UtcDateTime,
            ExpiresUtc = clock.GetUtcNow().UtcDateTime.AddDays(30), ReportJson = JsonSerializer.Serialize(report) };
        await repository.SaveReportShareAsync(share, ct);
        return new(share.Id, share.Token, tenant.TenantId, share.ExpiresUtc, false, request.Year, request.Month);
    }
}
public class RevokeQualityCycleReportShareHandler(IQualityCycleRepository repository) : IRequestHandler<RevokeQualityCycleReportShareCommand>
{
    public async Task Handle(RevokeQualityCycleReportShareCommand request, CancellationToken ct) {
        var share = await repository.GetReportShareAsync(request.Id, ct) ?? throw new NotFoundException("Report", request.Id);
        share.IsRevoked = true; await repository.SaveReportShareAsync(share, ct);
    }
}
public class ListQualityCycleReportSharesHandler(IQualityCycleRepository repository, ITenantContext tenant)
    : IRequestHandler<ListQualityCycleReportSharesQuery, IReadOnlyList<QualityCycleReportShareDto>>
{
    public async Task<IReadOnlyList<QualityCycleReportShareDto>> Handle(ListQualityCycleReportSharesQuery request, CancellationToken ct) =>
        (await repository.ListReportSharesAsync(ct)).Select(s => {
            var report = JsonSerializer.Deserialize<QualityCycleMonthlyReport>(s.ReportJson)!;
            return new QualityCycleReportShareDto(s.Id, s.Token, tenant.TenantId, s.ExpiresUtc, s.IsRevoked, report.Year, report.Month);
        }).ToList();
}
public class GetSharedQualityCycleReportPdfHandler(IQualityCycleRepository repository, IQualityCyclePdfGenerator pdf, TimeProvider clock)
    : IRequestHandler<GetSharedQualityCycleReportPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetSharedQualityCycleReportPdfQuery request, CancellationToken ct) {
        var share = await repository.GetReportShareByTokenAsync(request.Token, ct);
        if (share is null || share.IsRevoked || share.ExpiresUtc <= clock.GetUtcNow().UtcDateTime)
            throw new NotFoundException("Report", Guid.Empty);
        return pdf.GenerateMonthlyReportPdf(JsonSerializer.Deserialize<QualityCycleMonthlyReport>(share.ReportJson)!);
    }
}
