using System.Net;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Infrastructure.Partners;
using Microsoft.Extensions.Configuration;
namespace CleaningSuite.Tests.Partners;

public class PartnerNotificationTests
{
    [Fact]
    public async Task Retry_preserves_provider_idempotency_key_and_payload()
    {
        var handler = new CaptureHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var transport = new PartnerNotificationTransport(new(handler), Configuration());
        var delivery = new PartnerNotificationDelivery { Id=Guid.NewGuid(), To="owner@example.test", Subject="Update", Html="<p>Sign in</p>" };
        var failure=await Assert.ThrowsAsync<PartnerNotificationSendException>(()=>transport.SendAsync(delivery,default));
        Assert.True(failure.Retryable); await transport.SendAsync(delivery,default);
        Assert.Equal(handler.Payloads[0],handler.Payloads[1]); Assert.Equal(handler.Keys[0],handler.Keys[1]);
        Assert.Equal($"partner/{delivery.Id:N}",handler.Keys[0]);
    }
    [Theory][InlineData(400,false)][InlineData(401,false)][InlineData(429,true)][InlineData(503,true)]
    public async Task Provider_errors_are_classified_without_exposing_response_body(int status,bool retryable)
    {
        var transport=new PartnerNotificationTransport(new(new CaptureHandler((HttpStatusCode)status)),Configuration());
        var failure=await Assert.ThrowsAsync<PartnerNotificationSendException>(()=>transport.SendAsync(new(){Id=Guid.NewGuid()},default));
        Assert.Equal(retryable,failure.Retryable); Assert.DoesNotContain("private-provider-response",failure.Message);
    }
    [Fact]
    public void Current_role_company_and_lifecycle_limit_recipients()
    {
        var company=Guid.NewGuid(); var notification=new PartnerNotification {CompanyId=company,Audience="Company"};
        var owner=new Employee {Role=Employee.RolePartner,PartnerCompanyId=company};
        Assert.True(PartnerNotificationDispatcher.Eligible(notification,owner,true));
        Assert.False(PartnerNotificationDispatcher.Eligible(notification,owner,false));
        owner.PartnerCompanyId=Guid.NewGuid(); Assert.False(PartnerNotificationDispatcher.Eligible(notification,owner,true));
        owner.PartnerCompanyId=company;owner.IsActive=false;Assert.False(PartnerNotificationDispatcher.Eligible(notification,owner,true));
        owner.IsActive=true;owner.Role=Employee.RoleEmployee;Assert.False(PartnerNotificationDispatcher.Eligible(notification,owner,true));
        notification.Audience="Staff";owner.Role=Employee.RoleManager;Assert.True(PartnerNotificationDispatcher.Eligible(notification,owner,true));
        owner.IdentitySyncPending=true;Assert.False(PartnerNotificationDispatcher.Eligible(notification,owner,true));
    }
    [Fact]
    public void Withdrawals_and_cancellations_are_delivered_but_stale_offers_are_suppressed()
    {
        var employee = Guid.NewGuid();
        var request = new PartnerCleaningRequest { Status="Offered", Revision=3, OfferedEmployeeId=employee };
        var message = new PartnerNotification { Kind="OfferCreated", Revision=3, RecipientEmployeeId=employee };
        Assert.True(PartnerNotificationDispatcher.CurrentForRequest(message,request));
        request.Status="Cancelled"; request.Revision++;
        Assert.False(PartnerNotificationDispatcher.CurrentForRequest(message,request));
        message.Kind="RequestCancelled";Assert.True(PartnerNotificationDispatcher.CurrentForRequest(message,request));
        request.Status="Requested";message.Kind="OfferWithdrawn";Assert.True(PartnerNotificationDispatcher.CurrentForRequest(message,request));
        request.Status="Offered";Assert.False(PartnerNotificationDispatcher.CurrentForRequest(message,request));
    }
    [Fact]
    public void Automatic_acceptance_information_requires_current_accepted_employee_and_revision()
    {
        var actor = Guid.NewGuid();
        var request = new PartnerCleaningRequest { Status = "Accepted", Revision = 4, AcceptedEmployeeId = actor };
        var notice = new PartnerNotification { Kind = "WorkAutoAccepted", Revision = 4, RecipientEmployeeId = actor };
        Assert.True(PartnerNotificationDispatcher.CurrentForRequest(notice, request));
        request.Revision++; Assert.False(PartnerNotificationDispatcher.CurrentForRequest(notice, request));
        request.Revision--; request.AcceptedEmployeeId = Guid.NewGuid(); Assert.False(PartnerNotificationDispatcher.CurrentForRequest(notice, request));
        request.AcceptedEmployeeId = actor; request.Status = "Offered"; Assert.False(PartnerNotificationDispatcher.CurrentForRequest(notice, request));
    }
    private static IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Resend:ApiKey","synthetic-key"}}).Build();
    private sealed class CaptureHandler(params HttpStatusCode[] statuses):HttpMessageHandler
    {
        public List<string> Keys=[];public List<string> Payloads=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());Payloads.Add(await request.Content!.ReadAsStringAsync(ct));
            return new(statuses[Math.Min(Keys.Count-1,statuses.Length-1)]){Content=new StringContent("private-provider-response")};
        }
    }
}
