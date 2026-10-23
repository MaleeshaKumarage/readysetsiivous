using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CleaningSuite.Api;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Leads;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
namespace CleaningSuite.Tests.Leads;
public class LeadCollaborationHttpTests
{
    [Theory][InlineData("admin",200)][InlineData("manager",200)][InlineData("employee",403)][InlineData("",401)]
    public async Task Collaboration_roles_and_original_save_contract(string role,int expected)
    {
        var f=new LeadCollaborationTests.Fixture();
        f.Leads.Setup(x=>x.ListAsync("","Active",0,25,It.IsAny<CancellationToken>())).ReturnsAsync(((IReadOnlyList<Lead>)[f.Lead],1));
        f.Repository.Setup(x=>x.VoicesAsync(f.Lead.Id,0,25,It.IsAny<CancellationToken>())).ReturnsAsync(((IReadOnlyList<LeadVoiceNote>)f.Voices,0));
        // This server fixture accepts the request cancellation token, unlike the deterministic service fixture.
        f.Leads.Setup(x=>x.GetAsync(f.Lead.Id,It.IsAny<CancellationToken>())).ReturnsAsync(f.Lead);
        f.Leads.Setup(x=>x.LockAsync(f.Lead.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        f.Probe.Setup(x=>x.ValidateAsync(It.IsAny<byte[]>(),It.IsAny<CancellationToken>())).ReturnsAsync(new LeadAudioInfo(".webm","audio/webm",1));
        f.Repository.Setup(x=>x.VoiceAsync(f.Lead.Id,It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid leadId,Guid noteId,CancellationToken ct)=>f.Voices.SingleOrDefault(x=>x.Id==noteId));
        f.Repository.Setup(x=>x.SaveVoiceAsync(It.IsAny<LeadVoiceNote>(),It.IsAny<CancellationToken>())).Callback((LeadVoiceNote note,CancellationToken ct)=>{note.Version=Guid.NewGuid();f.Voices.Add(note);}).Returns(Task.CompletedTask);
        f.Repository.Setup(x=>x.DeleteVoiceAsync(It.IsAny<LeadVoiceNote>(),It.IsAny<CancellationToken>())).Callback((LeadVoiceNote note,CancellationToken ct)=>f.Voices.Remove(note)).Returns(Task.CompletedTask);
        f.Audio.Setup(x=>x.ReadAsync(It.IsAny<LeadVoiceNote>(),It.IsAny<CancellationToken>())).ReturnsAsync(new byte[]{1,2});
        var employees=new Mock<IEmployeeRepository>();if(role is "manager" or "employee") employees.Setup(x=>x.GetByKeycloakUserIdAsync("test-sub",It.IsAny<CancellationToken>())).ReturnsAsync(new Employee{Role=role,FirstName="Verified",LastName="Manager",KeycloakUserId="test-sub"});
        f.Registry.Setup(x=>x.GetBySlugAsync("tenant-a",It.IsAny<CancellationToken>())).ReturnsAsync(f.Registration);
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["BackgroundWorkers:Enabled"]="false",["Auth:Keycloak:ServerUrl"]="https://auth.test",["ConnectionStrings:DefaultConnection"]="Host=localhost;Database=synthetic;Username=synthetic;Password=synthetic"}));
            builder.ConfigureTestServices(services=>{
                foreach(var descriptor in services.Where(x=>x.ImplementationType==typeof(MartenSchemaInitializer)).ToList()) services.Remove(descriptor);
                services.RemoveAll<ILeadRepository>();services.AddSingleton(f.Leads.Object);
                services.RemoveAll<ILeadCollaborationRepository>();services.AddSingleton(f.Repository.Object);
                services.RemoveAll<ILeadAudioStore>();services.AddSingleton(f.Audio.Object);
                services.RemoveAll<ILeadAudioProbe>();services.AddSingleton(f.Probe.Object);
                services.RemoveAll<ILeadAdminDirectory>();services.AddSingleton(f.Directory.Object);
                services.RemoveAll<IEmployeeRepository>();services.AddSingleton(employees.Object);
                services.RemoveAll<ITenantRegistry>();services.AddSingleton(f.Registry.Object);
                services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="leads-http";o.DefaultChallengeScheme="leads-http";o.DefaultForbidScheme="leads-http";}).AddScheme<AuthenticationSchemeOptions,Authentication>("leads-http",_=>{});
            });
        });
        using var client=factory.CreateClient();if(role.Length>0) client.DefaultRequestHeaders.Add("X-Test-Role",role);
        Assert.Equal((HttpStatusCode)expected,(await client.GetAsync("/api/v1/manager/leads")).StatusCode);
        Assert.Equal((HttpStatusCode)expected,(await client.GetAsync($"/api/v1/manager/leads/{f.Lead.Id}")).StatusCode);
        if(role is "admin" or "manager") {
            var detail=await (await client.GetAsync($"/api/v1/manager/leads/{f.Lead.Id}")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(role=="admin",detail.GetProperty("capabilities").GetProperty("canEditMetadata").GetBoolean());
        }
        // Real MVC binding/CreatedAtAction coverage: POST must return 201 and a usable route URL,
        // rather than only testing mocked frontend success responses.
        var save=await client.PostAsJsonAsync("/api/v1/admin/leads",new LeadFields{Name="Synthetic HTTP create"});
        Assert.Equal(role=="admin"?HttpStatusCode.Created:role==""?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,save.StatusCode);
        if(role=="admin") Assert.Contains("/api/v1/admin/leads/",save.Headers.Location!.ToString());
        if(role is "admin" or "manager") {
            var recordingId=Guid.NewGuid();using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent([1,2]),"file","recording");form.Add(new StringContent("Spoofed name"),"authorName");
            var response=await client.PostAsync($"/api/v1/manager/leads/{f.Lead.Id}/voice-notes/{recordingId}",form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var note=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();Assert.Equal(role=="manager"?"Verified Manager":"Verified Admin",note.GetProperty("authorName").GetString());
            var download=await client.GetAsync($"/api/v1/manager/leads/{f.Lead.Id}/voice-notes/{recordingId}/audio");Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.True(download.Headers.CacheControl!.NoStore);Assert.Equal("audio/webm",download.Content.Headers.ContentType!.MediaType);
            using var remove=new HttpRequestMessage(HttpMethod.Delete,$"/api/v1/manager/leads/{f.Lead.Id}/voice-notes/{recordingId}"){Content=JsonContent.Create(new{version=note.GetProperty("version").GetGuid()})};
            Assert.Equal(role=="admin"?HttpStatusCode.NoContent:HttpStatusCode.Forbidden,(await client.SendAsync(remove)).StatusCode);
        }
        using var delete=new HttpRequestMessage(HttpMethod.Delete,$"/api/v1/manager/leads/{f.Lead.Id}/voice-notes/{Guid.NewGuid()}"){Content=JsonContent.Create(new{version=Guid.NewGuid()})};
        if(role!="admin") Assert.Equal(role==""?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,(await client.SendAsync(delete)).StatusCode);
    }
    public sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role=Request.Headers["X-Test-Role"].ToString();if(role.Length==0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity=new ClaimsIdentity([new("sub","test-sub"),new("email_verified","true"),new("name","Verified Admin"),new("iss","https://auth.test/realms/tenant-a"),new("realm_access",System.Text.Json.JsonSerializer.Serialize(new{roles=new[]{role}}))],"leads-http");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),"leads-http")));
        }
    }
}
