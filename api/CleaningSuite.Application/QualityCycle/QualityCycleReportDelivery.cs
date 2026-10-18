using System.Text.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Application.QualityCycle;

public record SendQualityCycleReportCommand(QualityCycleReportSelection Selection, string Email) : IRequest<QualityCycleReportShareDto>;
public record RetryQualityCycleReportEmailCommand(Guid Id) : IRequest<QualityCycleReportShareDto>;
public record GetSharedQualityCycleReportQuery(string Token) : IRequest<PublicQualityCycleReportDto>;
public record AcknowledgeQualityCycleReportCommand(string Token) : IRequest<PublicQualityCycleReportDto>;
public record PublicQualityCycleReportDto(string Language, DateTime ExpiresUtc, DateTime? AcknowledgedUtc,
    IReadOnlyList<QualityCycleReportPeriod>? Periods, int Year, int Month, IReadOnlyList<PublicQualityCycleReportRow> Shifts);
public record PublicQualityCycleReportRow(string CompanyName, string BranchName, string ShiftName,
    int ScheduledOccurrences, int CompletedOccurrences, decimal ScheduledHours, decimal? ActualHours, int? IncompleteTimingRecords);

public static class ReportShareMapping
{
    public static QualityCycleReportShareDto Dto(QualityCycleReportShare share, QualityCycleMonthlyReport report, string tenant) =>
        new(share.Id, share.Token, tenant, share.ExpiresUtc, share.IsRevoked, report.Year, report.Month) {
            RecipientEmail = share.RecipientEmail, EmailSentUtc = share.EmailSentUtc, EmailError = share.EmailError,
            AcknowledgedUtc = share.AcknowledgedUtc, Language = report.Language, Periods = report.Periods
        };
    public static PublicQualityCycleReportDto Public(QualityCycleReportShare share) {
        var report = JsonSerializer.Deserialize<QualityCycleMonthlyReport>(share.ReportJson)!;
        return new(report.Language, share.ExpiresUtc, share.AcknowledgedUtc, report.Periods, report.Year, report.Month,
            report.Shifts.Select(s => new PublicQualityCycleReportRow(s.CompanyName, s.BranchName, s.ShiftName,
                s.ScheduledOccurrences, s.CompletedOccurrences, s.ScheduledHours, s.RecordedStaffHours, s.IncompleteTimingRecords)).ToArray());
    }
    public static QualityCycleReportShare RequireAvailable(QualityCycleReportShare? share, DateTime now) =>
        share is null || share.IsRevoked || share.ExpiresUtc <= now ? throw new NotFoundException("Report", Guid.Empty) : share;
}

public class QualityCycleReportDelivery(IQualityCycleRepository repository, IEmailSender email,
    IConfiguration config, ITenantContext tenant, TimeProvider clock)
{
    public async Task<QualityCycleReportShareDto> SendAsync(QualityCycleReportShare share, CancellationToken ct)
    {
        ReportShareMapping.RequireAvailable(share, clock.GetUtcNow().UtcDateTime);
        if (string.IsNullOrWhiteSpace(share.RecipientEmail)) throw new QualityCycleConflictException("This report has no email recipient.");
        var report = JsonSerializer.Deserialize<QualityCycleMonthlyReport>(share.ReportJson)!;
        if (share.EmailSentUtc.HasValue) return ReportShareMapping.Dto(share, report, tenant.TenantId);
        try
        {
            var origin = config["App:FrontendBaseUrl"];
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
                || uri.AbsolutePath != "/" || string.IsNullOrWhiteSpace(config["Resend:ApiKey"]))
                throw new InvalidOperationException("Report email is not configured.");
            var link = $"{uri.GetLeftPart(UriPartial.Authority)}/quality-report/?tenant={Uri.EscapeDataString(tenant.TenantId)}&token={Uri.EscapeDataString(share.Token)}";
            var content = QualityCycleReportEmail.Build(report, link, share.ExpiresUtc);
            await email.SendAsync(share.RecipientEmail, content.Subject, content.Html, ct);
            share.EmailSentUtc = clock.GetUtcNow().UtcDateTime;
            share.EmailError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Provider details remain in server logs, never expose credentials or raw responses.
            share.EmailError = "Email could not be sent. Check email configuration and retry.";
        }
        await repository.UpdateReportShareDeliveryAsync(share.Id, share.EmailSentUtc, share.EmailError, ct);
        var persisted = await repository.GetReportShareByTokenAsync(share.Token, ct) ?? share;
        return ReportShareMapping.Dto(persisted, report, tenant.TenantId);
    }
}
public class SendQualityCycleReportHandler(QualityCycleSelectedReportService reports, IQualityCycleRepository repository,
    QualityCycleReportDelivery delivery, TimeProvider clock) : IRequestHandler<SendQualityCycleReportCommand, QualityCycleReportShareDto>
{
    public async Task<QualityCycleReportShareDto> Handle(SendQualityCycleReportCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254
            || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email))
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("Email", "Enter a valid recipient email address.")]);
        var report = await reports.BuildAsync(request.Selection, ct);
        report = report with { Shifts = report.Shifts.Select(s => s with { Forms = Array.Empty<QualityCycleFormDto>() }).ToArray() };
        var now = clock.GetUtcNow().UtcDateTime;
        var share = new QualityCycleReportShare { ReportJson = JsonSerializer.Serialize(report), RecipientEmail = request.Email.Trim(),
            CreatedUtc = now, UpdatedUtc = now, ExpiresUtc = now.AddDays(30) };
        await repository.SaveReportShareAsync(share, ct);
        return await delivery.SendAsync(share, ct);
    }
}
public class RetryQualityCycleReportEmailHandler(IQualityCycleRepository repository, QualityCycleReportDelivery delivery)
    : IRequestHandler<RetryQualityCycleReportEmailCommand, QualityCycleReportShareDto>
{
    public async Task<QualityCycleReportShareDto> Handle(RetryQualityCycleReportEmailCommand request, CancellationToken ct) =>
        await delivery.SendAsync(await repository.GetReportShareAsync(request.Id, ct) ?? throw new NotFoundException("Report", request.Id), ct);
}
public class PublicReportHandler(IQualityCycleRepository repository, TimeProvider clock)
    : IRequestHandler<GetSharedQualityCycleReportQuery, PublicQualityCycleReportDto>, IRequestHandler<AcknowledgeQualityCycleReportCommand, PublicQualityCycleReportDto>
{
    public async Task<PublicQualityCycleReportDto> Handle(GetSharedQualityCycleReportQuery request, CancellationToken ct) =>
        ReportShareMapping.Public(ReportShareMapping.RequireAvailable(await repository.GetReportShareByTokenAsync(request.Token, ct), clock.GetUtcNow().UtcDateTime));
    public async Task<PublicQualityCycleReportDto> Handle(AcknowledgeQualityCycleReportCommand request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var share = ReportShareMapping.RequireAvailable(await repository.GetReportShareByTokenAsync(request.Token, ct), now);
        await repository.AcknowledgeReportShareAsync(request.Token, now, ct);
        // Read canonical persisted timestamp, including concurrent acknowledgment requests.
        return await Handle(new GetSharedQualityCycleReportQuery(request.Token), ct);
    }
}
