using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using Microsoft.Extensions.Configuration;
namespace CleaningSuite.Infrastructure.Leads;

public sealed class LeadReminderTransport(HttpClient http,IConfiguration config) : ILeadReminderTransport
{
    public async Task SendAsync(LeadFollowUpDelivery delivery,CancellationToken ct)
    {
        var key=config["Resend:ApiKey"];
        if(string.IsNullOrWhiteSpace(key)) throw new LeadReminderSendException("Reminder email is not configured. Ask an administrator to check Resend settings.",false);
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.resend.com/emails");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        request.Headers.Add("Idempotency-Key",$"lead-follow-up/{delivery.Id:N}");
        request.Content=JsonContent.Create(new { from=config["Resend:From"]??"ReadySetSiivous <noreply@readysetsiivous.fi>",to=new[]{delivery.RecipientEmail},subject=delivery.Subject,html=delivery.Html });
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response=await http.SendAsync(request,timeout.Token);
            if(response.IsSuccessStatusCode) return;
            var retry=response.Headers.RetryAfter?.Delta;
            if(!retry.HasValue && response.Headers.RetryAfter?.Date is { } date) retry=date-DateTimeOffset.UtcNow;
            var transient=response.StatusCode==HttpStatusCode.TooManyRequests || (int)response.StatusCode>=500 || response.StatusCode==HttpStatusCode.RequestTimeout;
            throw new LeadReminderSendException(transient?"Email provider temporarily unavailable; retry scheduled.":"Email provider rejected this reminder. Check sender and recipient configuration.",transient,retry);
        } catch(HttpRequestException) { throw new LeadReminderSendException("The email service could not be reached; retry scheduled.",true); }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new LeadReminderSendException("Email acceptance is uncertain after a timeout; retry uses the same delivery ID.",true); }
    }
}
