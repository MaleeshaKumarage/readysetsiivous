using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/leads")]
[Authorize(Policy=RealmRoleAuthorization.PolicyPrefix+"admin")]
public sealed class LeadsAdminController(LeadService leads) : ControllerBase
{
    public record EditRequest(Guid Version,LeadFields Fields);
    public record VersionRequest(Guid Version);
    [HttpGet] public async Task<IActionResult> List([FromQuery]string? search,[FromQuery]string? status,[FromQuery]int skip=0,[FromQuery]int take=25,CancellationToken ct=default)
        =>Ok(await leads.ListAsync(search,status,skip,take,ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id,CancellationToken ct)=>Ok(await leads.GetAsync(id,ct));
    [HttpPost] public async Task<IActionResult> Create([FromBody]LeadFields fields,CancellationToken ct)
    { var result=await leads.CreateAsync(fields,ct);return CreatedAtAction(nameof(Get),new{id=result.Lead.Id},result); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Edit(Guid id,[FromBody]EditRequest request,CancellationToken ct)=>Ok(await leads.UpdateAsync(id,request.Version,request.Fields,ct));
    [HttpPost("{id:guid}/activate")] public async Task<IActionResult> Activate(Guid id,[FromBody]VersionRequest request,CancellationToken ct)=>Ok(await leads.SetActiveAsync(id,request.Version,true,ct));
    [HttpPost("{id:guid}/deactivate")] public async Task<IActionResult> Deactivate(Guid id,[FromBody]VersionRequest request,CancellationToken ct)=>Ok(await leads.SetActiveAsync(id,request.Version,false,ct));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id,[FromBody]VersionRequest request,CancellationToken ct)
    { await leads.DeleteAsync(id,request.Version,ct);return NoContent(); }
    [HttpPost("{id:guid}/convert")] public async Task<IActionResult> Convert(Guid id,[FromBody]VersionRequest request,CancellationToken ct)=>Ok(await leads.ConvertAsync(id,request.Version,ct));
    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(6*1024*1024)]
    [RequestFormLimits(MultipartBodyLengthLimit=6*1024*1024)]
    public async Task<IActionResult> Upload(Guid id,[FromForm]Guid version,IFormFile file,CancellationToken ct)
    {
        if(file is null || file.Length==0) throw new FluentValidation.ValidationException("Choose a non-empty image.");
        if(file.Length>5*1024*1024) throw new UploadPayloadTooLargeException("Lead images must be 5 MiB or smaller.");
        await using var input=file.OpenReadStream();using var buffer=new MemoryStream();await input.CopyToAsync(buffer,ct);
        return Ok(await leads.UploadAsync(id,version,buffer.ToArray(),ct));
    }
    [HttpDelete("{id:guid}/images/{imageId:guid}")] public async Task<IActionResult> RemoveImage(Guid id,Guid imageId,[FromBody]VersionRequest request,CancellationToken ct)
        =>Ok(await leads.RemoveImageAsync(id,imageId,request.Version,ct));
    [HttpGet("{id:guid}/images/{imageId:guid}")] public async Task<IActionResult> Image(Guid id,Guid imageId,CancellationToken ct)
    { var image=await leads.ImageAsync(id,imageId,ct);Response.Headers.CacheControl="no-store";Response.Headers.XContentTypeOptions="nosniff";return File(image.Bytes,image.ContentType); }
}
