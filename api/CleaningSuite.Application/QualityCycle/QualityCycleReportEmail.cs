using System.Globalization;
using System.Net;
using CleaningSuite.Application.Shifts;

namespace CleaningSuite.Application.QualityCycle;

public record QualityCycleReportEmailContent(string Subject, string Html);

/// <summary>Email-safe markup derived only from the shared, immutable report snapshot.</summary>
public static class QualityCycleReportEmail
{
    public static QualityCycleReportEmailContent Build(QualityCycleMonthlyReport report, string link, DateTime expiresUtc)
    {
        string T(string key) => ReportText.Get(report.Language, key);
        static string E(string value) => WebUtility.HtmlEncode(value);
        var culture = CultureInfo.GetCultureInfo(report.Language switch { "fi" => "fi-FI", "sv" => "sv-SE", _ => "en-GB" });
        var periods = report.Periods is { Count: > 0 } ? report.Periods : [new QualityCycleReportPeriod(new(report.Year, report.Month, 1),
            new(report.Year, report.Month, DateTime.DaysInMonth(report.Year, report.Month)))];
        var period = string.Join("; ", periods.Select(p => p.From.Day == 1 && p.To == p.From.AddMonths(1).AddDays(-1)
            ? p.From.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", culture)
            : $"{p.From:dd.MM.yyyy} – {p.To:dd.MM.yyyy}"));
        var title = T("Cleaning Quality Report");
        var subject = period.Length <= 120 ? $"{title} - {period}" : title;
        var zone = TenantTimeZone.Resolve(report.TimeZoneId);
        var expires = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc), zone);
        var logo = new Uri(new Uri(link), "/brand/readyset-primary-logo.png").AbsoluteUri;
        var rows = string.Join("", report.Shifts.Select(s => (s.CompanyName, s.BranchName, s.ShiftName)).Distinct().Select(s =>
            $"<tr><td style=\"padding:10px 8px;border-bottom:1px solid #e2e6ef;vertical-align:top;\">{E(s.CompanyName)}</td>" +
            $"<td style=\"padding:10px 8px;border-bottom:1px solid #e2e6ef;vertical-align:top;\">{E(s.BranchName)}</td>" +
            $"<td style=\"padding:10px 8px;border-bottom:1px solid #e2e6ef;vertical-align:top;\">{E(s.ShiftName)}</td></tr>"));
        var shifts = report.Shifts.Count == 0 ? "" : $"""
            <h2 style="font-size:18px;margin:24px 0 10px;color:#0a1741;">{E(T("Included shifts"))}</h2>
            <table style="width:100%;border-collapse:collapse;font-size:13px;table-layout:fixed;"><thead><tr style="background:#f3f5fa;text-align:left;">
            <th style="padding:10px 8px;">{E(T("Company"))}</th><th style="padding:10px 8px;">{E(T("Branch"))}</th><th style="padding:10px 8px;">{E(T("Shift"))}</th></tr></thead><tbody>{rows}</tbody></table>
            """;
        var html = $"""
            <!doctype html><html lang="{E(report.Language)}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(subject)}</title></head>
            <body style="margin:0;padding:0;background:#f3f5fa;font-family:Arial,Helvetica,sans-serif;color:#17213b;line-height:1.6;">
            <div style="display:none;max-height:0;overflow:hidden;">{E(title)} · {E(period)}</div>
            <table role="presentation" style="width:100%;border-collapse:collapse;background:#f3f5fa;"><tr><td align="center" style="padding:24px 12px;">
            <table role="presentation" style="width:100%;max-width:620px;border-collapse:collapse;background:#ffffff;border:1px solid #e2e6ef;">
            <tr><td style="background:#0a1741;padding:24px;border-bottom:4px solid #ddc067;">
            <table role="presentation" style="width:100%;border-collapse:collapse;"><tr><td style="width:72px;vertical-align:middle;"><img src="{E(logo)}" alt="ReadySet Siivous" width="60" height="60" style="display:block;border:0;"></td>
            <td style="vertical-align:middle;color:#ffffff;"><p style="margin:0 0 4px;font-size:12px;letter-spacing:1px;color:#ddc067;">READYSET SIIVOUS</p><h1 style="margin:0;font-size:23px;line-height:1.3;color:#ffffff;">{E(title)}</h1></td></tr></table></td></tr>
            <tr><td style="padding:24px;">
            <p style="margin:0 0 20px;">{E(T("Your cleaning quality report is ready to review."))}</p>
            <table role="presentation" style="width:100%;border-collapse:collapse;background:#f3f5fa;"><tr><td style="padding:14px 16px;border-left:4px solid #ddc067;">
            <p style="margin:0;font-size:12px;font-weight:bold;color:#536078;">{E(T("Reporting period"))}</p><p style="margin:3px 0 0;font-size:18px;font-weight:bold;color:#0a1741;">{E(period)}</p></td></tr></table>
            {shifts}
            <h2 style="font-size:18px;margin:24px 0 8px;color:#0a1741;">{E(T("What is included"))}</h2>
            <p style="margin:0 0 12px;">{E(T("The report summarises scheduled and recorded visits, scheduled hours, recorded work hours and cleaning activities. Available photos and their notes are included in the PDF appendix."))}</p>
            <p style="margin:0 0 16px;">{E(T("Recorded activities show what was done. Unchecked activities are not automatically treated as failures."))}</p>
            <h2 style="font-size:18px;margin:24px 0 8px;color:#0a1741;">{E(T("Next steps"))}</h2>
            <ol style="padding-left:22px;margin:0 0 20px;"><li>{E(T("Open the report and review the summary."))}</li><li>{E(T("Download the PDF for the full details and photo appendix."))}</li><li>{E(T("Select Acknowledge receipt on the report page to confirm you received it."))}</li></ol>
            <table role="presentation" style="border-collapse:collapse;margin:24px 0;"><tr><td style="background:#ddc067;border-radius:6px;"><a href="{E(link)}" style="display:inline-block;padding:13px 24px;font-weight:bold;font-size:16px;color:#0a1741;text-decoration:none;">{E(T("Open report"))}</a></td></tr></table>
            <p style="margin:0 0 12px;font-size:13px;color:#536078;">{E(T("Acknowledgment confirms receipt of this report. It is not a signature or approval of its contents."))}</p>
            <p style="margin:0 0 20px;font-size:13px;"><strong>{E(T("Link available until"))}:</strong> {expires:dd.MM.yyyy HH:mm} ({E(report.TimeZoneId)})</p>
            <p style="margin:0;font-size:12px;color:#536078;">{E(T("If the button does not work, copy this link into your browser:"))}</p>
            <p style="margin:5px 0 0;font-size:12px;word-break:break-all;overflow-wrap:anywhere;"><a href="{E(link)}" style="color:#0a1741;">{E(link)}</a></p>
            </td></tr><tr><td style="padding:16px 24px;background:#f3f5fa;border-top:1px solid #e2e6ef;font-size:12px;color:#536078;">{E(T("For questions about this report, contact your usual cleaning service contact."))}<br><strong style="color:#0a1741;">ReadySet Siivous</strong></td></tr></table>
            </td></tr></table></body></html>
            """;
        return new(subject, html);
    }
}
