using System.Security.Claims;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Leads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/manager/leads")]
[Authorize(Policy=RealmRoleAuthorization.PolicyPrefix+"operations")]
public sealed class LeadCollaborationController(LeadService leads,LeadCollaborationService collaboration,ILeadAdminDirectory directory,
    ITenantProfileRepository profiles,ITenantContext tenant) : ControllerBase
{
    public record EditFollowUpRequest(Guid Version,LeadFollowUpInput Input);
    public record VersionRequest(Guid Version);
    private bool Admin=>RealmRoleAuthorization.HasRole(User,"admin");
    private LeadActor Actor {
        get {
            var employee=HttpContext.Items["Employee"] as Employee;
            var name=employee is null?User.FindFirstValue("name")??User.FindFirstValue("preferred_username")??User.FindFirstValue("email")??"Tenant administrator"
                :$"{employee.FirstName} {employee.LastName}".Trim();
            return new(User.FindFirstValue("sub")??throw new UnauthorizedAccessException(),name.Length>200?name[..200]:name);
        }
    }
    private static object Voice(LeadVoiceNote note)=>new { note.Id,note.Version,note.AuthorName,note.CreatedUtc,note.DurationSeconds,note.ContentType };
    [HttpGet] public async Task<IActionResult> List([FromQuery]string? search,[FromQuery]string? status,[FromQuery]int skip=0,[FromQuery]int take=25,CancellationToken ct=default)=>Ok(await leads.ListAsync(search,status,skip,take,ct));
    [HttpGet("settings")] public async Task<IActionResult> Settings(CancellationToken ct) { var (id,_)=await TenantTimeZone.LoadAsync(profiles,tenant,ct);return Ok(new { timeZoneId=id }); }
    [HttpGet("recipients")] public async Task<IActionResult> Recipients(CancellationToken ct)=>Ok(await directory.ListAsync(ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Detail(Guid id,CancellationToken ct) {
        var detail=await leads.GetAsync(id,ct);var active=detail.Lead.IsActive && detail.Lead.CompanyId is null && detail.Lead.Status!="Lost";
        return Ok(new { detail.Lead,detail.Images,detail.CustomerAvailable,capabilities=new { canEditMetadata=Admin,canRecord=active,canSchedule=active,canDeleteVoice=Admin && detail.Lead.CompanyId is null } });
    }
    [HttpGet("{id:guid}/images/{imageId:guid}")] public async Task<IActionResult> Image(Guid id,Guid imageId,CancellationToken ct) {
        var image=await leads.ImageAsync(id,imageId,ct);Response.Headers.CacheControl="no-store";Response.Headers.XContentTypeOptions="nosniff";return File(image.Bytes,image.ContentType);
    }
    [HttpGet("{id:guid}/voice-notes")] public async Task<IActionResult> Voices(Guid id,[FromQuery]int skip=0,[FromQuery]int take=25,CancellationToken ct=default) {
        var result=await collaboration.VoicesAsync(id,skip,take,ct);return Ok(new { items=result.Items.Select(Voice),result.Total });
    }
    [HttpPost("{id:guid}/voice-notes/{recordingId:guid}")]
    [RequestSizeLimit(21*1024*1024)]
    [RequestFormLimits(MultipartBodyLengthLimit=21*1024*1024)]
    public async Task<IActionResult> Record(Guid id,Guid recordingId,IFormFile file,CancellationToken ct) {
        if(file is null || file.Length==0) throw new FluentValidation.ValidationException("Record audio before saving.");
        if(file.Length>20*1024*1024) throw new CleaningSuite.Application.Common.UploadPayloadTooLargeException("Recordings must be 20 MiB or smaller.");
        await using var input=file.OpenReadStream();using var buffer=new MemoryStream();await input.CopyToAsync(buffer,ct);
        return Ok(Voice(await collaboration.SaveVoiceAsync(id,recordingId,buffer.ToArray(),Actor,ct)));
    }
    [HttpGet("{id:guid}/voice-notes/{noteId:guid}/audio")] public async Task<IActionResult> Audio(Guid id,Guid noteId,CancellationToken ct) {
        var value=await collaboration.ReadVoiceAsync(id,noteId,ct);Response.Headers.CacheControl="no-store";Response.Headers.XContentTypeOptions="nosniff";
        return File(value.Bytes,value.ContentType,enableRangeProcessing:true);
    }
    [HttpDelete("{id:guid}/voice-notes/{noteId:guid}")]
    [Authorize(Policy=RealmRoleAuthorization.PolicyPrefix+"admin")]
    public async Task<IActionResult> DeleteVoice(Guid id,Guid noteId,[FromBody]VersionRequest request,CancellationToken ct) { await collaboration.DeleteVoiceAsync(id,noteId,request.Version,ct);return NoContent(); }
    [HttpGet("{id:guid}/follow-ups")] public async Task<IActionResult> FollowUps(Guid id,[FromQuery]int skip=0,[FromQuery]int take=25,CancellationToken ct=default)=>Ok(await collaboration.FollowUpsAsync(id,skip,take,ct));
    [HttpGet("{id:guid}/deliveries")] public async Task<IActionResult> History(Guid id,[FromQuery]int skip=0,[FromQuery]int take=25,CancellationToken ct=default) {
        var page=await collaboration.HistoryAsync(id,skip,take,ct);
        return Ok(new { page.Total,items=page.Items.Select(x=>new { x.Id,x.FollowUpId,x.RecipientName,x.RecipientEmail,x.LocalDate,x.TimeZoneId,x.DueUtc,x.State,x.Attempts,x.SentUtc,x.Error }) });
    }
    [HttpPost("{id:guid}/follow-ups")] public async Task<IActionResult> Create(Guid id,[FromBody]LeadFollowUpInput input,CancellationToken ct)=>Ok(await collaboration.SaveFollowUpAsync(id,null,null,input,Actor,ct));
    [HttpPut("{id:guid}/follow-ups/{followUpId:guid}")] public async Task<IActionResult> Edit(Guid id,Guid followUpId,[FromBody]EditFollowUpRequest request,CancellationToken ct)=>Ok(await collaboration.SaveFollowUpAsync(id,followUpId,request.Version,request.Input,Actor,ct));
    [HttpPost("{id:guid}/follow-ups/{followUpId:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id,Guid followUpId,[FromBody]VersionRequest request,CancellationToken ct) { await collaboration.CancelAsync(id,followUpId,request.Version,ct);return NoContent(); }
}
