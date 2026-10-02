namespace CleaningSuite.Application.Common;

/// <summary>Sends transactional email (invites, notifications).</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string html, CancellationToken ct = default);
}
