using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CleaningSuite.Tests.QualityCycle;
public class QualityCycleAuthorizationTests
{
    [Theory]
    [InlineData("employee", 403)] [InlineData("manager", 403)] [InlineData("", 401)] [InlineData("admin", 200)]
    public async Task OnlyAuthenticatedTenantAdminCanManageTemplates(string role, int expected)
    {
        var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(x => x.ListTemplatesAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<QualityCycleTemplate>());
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.test" }));
            builder.ConfigureTestServices(services => {
                foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<CleaningSuite.Application.Employees.IEmployeeRepository>(); services.AddSingleton(new Mock<CleaningSuite.Application.Employees.IEmployeeRepository>().Object);
                services.RemoveAll<IQualityCycleRepository>(); services.AddSingleton(repository.Object);
                services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "qc-test"; options.DefaultChallengeScheme = "qc-test"; options.DefaultForbidScheme = "qc-test"; })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("qc-test", _ => { });
            });
        });
        using var client = factory.CreateClient();
        if (role.Length > 0) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal((HttpStatusCode)expected, (await client.GetAsync("/api/v1/admin/quality-cycle/templates")).StatusCode);
        if (role != "admin") {
            foreach (var route in new[] { "/api/v1/admin/quality-cycle/monthly-summary?year=2026&month=10", "/api/v1/admin/quality-cycle/report-shares", "/api/v1/admin/quality-cycle/summary-pdf?year=2026&month=10&shiftId=" + Guid.NewGuid() })
                Assert.Equal((HttpStatusCode)expected, (await client.GetAsync(route)).StatusCode);
            Assert.Equal((HttpStatusCode)expected, (await client.PostAsync("/api/v1/admin/quality-cycle/report-shares", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        }
    }
    [Theory]
    [InlineData("employee",403)] [InlineData("manager",403)] [InlineData("",401)] [InlineData("admin",200)]
    public async Task Lead_routes_and_private_images_are_admin_only(string role,int expected)
    {
        var leads=new Mock<CleaningSuite.Application.Leads.ILeadRepository>();
        var leadFiles=new Mock<CleaningSuite.Application.Leads.ILeadFileStore>();
        leads.Setup(r=>r.ListAsync("","Active",0,25,It.IsAny<CancellationToken>())).ReturnsAsync(((IReadOnlyList<CleaningSuite.Domain.Leads.Lead>)Array.Empty<CleaningSuite.Domain.Leads.Lead>(),0));
        var registry=new Mock<ITenantRegistry>();registry.Setup(r=>r.GetBySlugAsync("tenant-a",It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration{Slug="tenant-a",Status=TenantRegistration.StatusActive});
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Auth:Keycloak:ServerUrl"]="https://auth.test"}));
            builder.ConfigureTestServices(services=>{
                foreach(var descriptor in services.Where(x=>x.ImplementationType==typeof(MartenSchemaInitializer)||x.ImplementationType==typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<CleaningSuite.Application.Leads.ILeadRepository>();services.AddSingleton(leads.Object);
                services.RemoveAll<CleaningSuite.Application.Leads.ILeadFileStore>();services.AddSingleton(leadFiles.Object);
                services.RemoveAll<ITenantRegistry>();services.AddSingleton(registry.Object);
                services.RemoveAll<CleaningSuite.Application.Employees.IEmployeeRepository>();services.AddSingleton(new Mock<CleaningSuite.Application.Employees.IEmployeeRepository>().Object);
                services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="lead-test";o.DefaultChallengeScheme="lead-test";o.DefaultForbidScheme="lead-test";}).AddScheme<AuthenticationSchemeOptions,TestAuthentication>("lead-test",_=>{});
            });
        });
        using var client=factory.CreateClient();if(role.Length>0) client.DefaultRequestHeaders.Add("X-Test-Role",role);
        Assert.Equal((HttpStatusCode)expected,(await client.GetAsync("/api/v1/admin/leads")).StatusCode);
        if(role=="admin") await VerifyAdminLeadImageHttpContract(client,leads,leadFiles);
        var id=Guid.NewGuid();
        if(role!="admin") {
            foreach(var (method,path) in new[]{("GET",$"/{id}/images/{id}"),("POST",""),("PUT",$"/{id}"),("POST",$"/{id}/convert"),("DELETE",$"/{id}"),("POST",$"/{id}/images"),("DELETE",$"/{id}/images/{id}"),("POST",$"/{id}/activate"),("POST",$"/{id}/deactivate")}) {
                using var request=new HttpRequestMessage(new HttpMethod(method),"/api/v1/admin/leads"+path){Content=method=="POST" && path.EndsWith("/images") ? new System.Net.Http.MultipartFormDataContent() : new StringContent("{}",System.Text.Encoding.UTF8,"application/json")};
                Assert.Equal((HttpStatusCode)expected,(await client.SendAsync(request)).StatusCode);
            }
            Assert.Empty(leads.Invocations);
        }
    }

    private static async Task VerifyAdminLeadImageHttpContract(HttpClient client,Mock<CleaningSuite.Application.Leads.ILeadRepository> repository,Mock<CleaningSuite.Application.Leads.ILeadFileStore> files)
    {
        var lead=new CleaningSuite.Domain.Leads.Lead{Version=Guid.NewGuid(),Fields=new(){Name="Image binding fixture"}};
        repository.Setup(x=>x.GetAsync(lead.Id,It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        repository.Setup(x=>x.LockAsync(lead.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        repository.Setup(x=>x.SaveAsync(lead,It.IsAny<CancellationToken>())).Callback(()=>lead.Version=Guid.NewGuid()).Returns(Task.CompletedTask);
        using var bitmap=new SkiaSharp.SKBitmap(2,2);using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);var bytes=encoded.ToArray();
        using var form=new System.Net.Http.MultipartFormDataContent();form.Add(new StringContent(lead.Version.ToString()),"version");
        var part=new ByteArrayContent(bytes);part.Headers.ContentType=new("image/png");form.Add(part,"file","fixture.png");
        var response=await client.PostAsync($"/api/v1/admin/leads/{lead.Id}/images",form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var attached=Assert.Single(lead.Images);files.Setup(x=>x.ReadAsync(lead.Id,attached,It.IsAny<CancellationToken>())).ReturnsAsync(bytes);
        var download=await client.GetAsync($"/api/v1/admin/leads/{lead.Id}/images/{attached.Id}");Assert.Equal(HttpStatusCode.OK,download.StatusCode);
        Assert.Equal("image/png",download.Content.Headers.ContentType!.MediaType);Assert.True(download.Headers.CacheControl!.NoStore);Assert.Equal(bytes,await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/admin/leads/{lead.Id}/images/{Guid.NewGuid()}")).StatusCode);
        using var invalid=new System.Net.Http.MultipartFormDataContent();invalid.Add(new StringContent(lead.Version.ToString()),"version");invalid.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("<svg/>")),"file","pretending.png");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync($"/api/v1/admin/leads/{lead.Id}/images",invalid)).StatusCode);
        using var remove=new HttpRequestMessage(HttpMethod.Delete,$"/api/v1/admin/leads/{lead.Id}/images/{attached.Id}"){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{version=lead.Version}),System.Text.Encoding.UTF8,"application/json")};
        Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(remove)).StatusCode);Assert.Empty(lead.Images);
    }

    public class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim("sub", "test-sub"), new Claim("iss", "https://auth.test/realms/tenant-a"), new Claim("realm_access", System.Text.Json.JsonSerializer.Serialize(new { roles = new[] { role } })) }, "qc-test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "qc-test")));
        }
    }
}
