using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace CleaningSuite.Infrastructure.Leads;

public sealed class LeadAudioProbe(IConfiguration config) : ILeadAudioProbe
{
    public async Task<LeadAudioInfo> ValidateAsync(byte[] content,CancellationToken ct)
    {
        if(content.Length is 0 or >20*1024*1024) throw new ValidationException("Record audio up to 20 MiB.");
        var path=Path.Combine(Path.GetTempPath(),$"lead-audio-{Guid.NewGuid():N}.bin");
        try {
            await File.WriteAllBytesAsync(path,content,ct);
            var json=await RunAsync(config["LeadAudio:Ffprobe"]??"ffprobe",["-v","error","-protocol_whitelist","file,pipe","-format_whitelist","matroska,webm,mov,ogg","-show_streams","-show_format","-of","json",path],128*1024,ct);
            using var doc=JsonDocument.Parse(json);
            var streams=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if(streams.Length!=1 || streams[0].GetProperty("codec_type").GetString()!="audio") throw new ValidationException("Only audio recordings are supported.");
            var codec=streams[0].GetProperty("codec_name").GetString();
            var format=doc.RootElement.GetProperty("format").GetProperty("format_name").GetString()??"";
            var type=format.Contains("webm") && codec=="opus"?new LeadAudioInfo(".webm","audio/webm",0)
                :format.Contains("mov") && codec=="aac"?new LeadAudioInfo(".m4a","audio/mp4",0)
                :format=="ogg" && codec=="opus"?new LeadAudioInfo(".ogg","audio/ogg",0):throw new ValidationException("Record WebM/Opus, MP4/AAC or Ogg/Opus audio.");
            // MediaRecorder WebM often omits container duration. Decode bounded PCM to validate
            // real audio and measure its duration instead of trusting browser metadata.
            var pcm=await RunAsync(config["LeadAudio:Ffmpeg"]??"ffmpeg",["-v","error","-xerror","-protocol_whitelist","file,pipe","-format_whitelist","matroska,webm,mov,ogg","-i",path,"-t","301","-vn","-ac","1","-ar","8000","-f","s16le","pipe:1"],5*1024*1024,ct);
            var duration=pcm.Length/16000d;
            if(duration is <=0 or >300) throw new ValidationException("A recording must contain audio and be no longer than five minutes.");
            return type with { DurationSeconds=duration };
        } catch(System.ComponentModel.Win32Exception) { throw new LeadConflictException("Voice validation is unavailable. Ask an administrator to check the audio runtime configuration, then retry."); }
        catch(JsonException) { throw new ValidationException("This recording is not readable audio. Record it again."); }
        finally { File.Delete(path); }
    }
    private static async Task<byte[]> RunAsync(string executable,IReadOnlyList<string> args,int maxBytes,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(var arg in args) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException("Audio process could not start.");
        try {
            // Drain stderr without retaining untrusted media text or filling the OS pipe.
            var drain=process.StandardError.BaseStream.CopyToAsync(Stream.Null,deadline.Token);
            using var result=new MemoryStream();var buffer=new byte[16384];int count;
            while((count=await process.StandardOutput.BaseStream.ReadAsync(buffer,deadline.Token))>0) {
                if(result.Length+count>maxBytes) throw new ValidationException("This recording exceeds the media validation limits.");
                await result.WriteAsync(buffer.AsMemory(0,count),deadline.Token);
            }
            await process.WaitForExitAsync(deadline.Token);await drain;
            if(process.ExitCode!=0) throw new ValidationException("This recording is unreadable or uses an unsupported audio format.");
            return result.ToArray();
        } catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new ValidationException("Audio validation timed out. Try a shorter recording."); }
        finally { if(!process.HasExited) { process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None); } }
    }
}
public sealed class DiskLeadAudioStore(IConfiguration config,ITenantContext tenant,ILogger<DiskLeadAudioStore> logger) : ILeadAudioStore
{
    private static readonly ConcurrentDictionary<string,int> SweepOffsets=new();
    private string Root=>Path.Combine(config["Uploads:Path"]??Path.Combine(Directory.GetCurrentDirectory(),"uploads"),"lead-audio",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenant.TenantId))).ToLowerInvariant());
    private string PathFor(LeadVoiceNote note)
    {
        if(note.Extension is not (".webm" or ".m4a" or ".ogg")) throw new LeadConflictException("Unsupported saved recording format.");
        return Path.Combine(Root,note.LeadId.ToString("N"),note.Id.ToString("N")+note.Extension);
    }
    public async Task SaveAsync(LeadVoiceNote note,byte[] content,CancellationToken ct)
    {
        var path=PathFor(note);Directory.CreateDirectory(Path.GetDirectoryName(path)!);var created=false;
        // Recover a crash between durable file creation and metadata commit without overwriting
        // a different recording. The caller holds the lead reservation.
        if(File.Exists(path)) {
            var existing=await File.ReadAllBytesAsync(path,ct);
            if(CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing),SHA256.HashData(content))) return;
            throw new LeadConflictException("A different recording already uses this ID. Discard it and record again.");
        }
        try { await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);created=true;await stream.WriteAsync(content,ct); }
        catch { if(created) await DeleteAsync(note);throw; }
    }
    public async Task<byte[]?> ReadAsync(LeadVoiceNote note,CancellationToken ct)
    { try { return await File.ReadAllBytesAsync(PathFor(note),ct); } catch(FileNotFoundException) { return null; } catch(DirectoryNotFoundException) { return null; } }
    public Task DeleteAsync(LeadVoiceNote note)
    { try { File.Delete(PathFor(note)); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { logger.LogError(e,"Lead audio cleanup failed for {LeadId}/{NoteId}",note.LeadId,note.Id); } return Task.CompletedTask; }
    public Task<IReadOnlyList<(Guid LeadId,Guid NoteId,string Extension)>> UnreferencedCandidatesAsync(DateTime beforeUtc,CancellationToken ct)
    {
        var result=new List<(Guid,Guid,string)>();var offset=SweepOffsets.GetValueOrDefault(Root);var visited=0;
        if(Directory.Exists(Root)) foreach(var directory in Directory.EnumerateDirectories(Root)) {
            ct.ThrowIfCancellationRequested();if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out var lead)) continue;
            foreach(var file in Directory.EnumerateFiles(directory)) {
                var ext=Path.GetExtension(file);
                if(ext is not (".webm" or ".m4a" or ".ogg") || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(file),"N",out var note) || File.GetLastWriteTimeUtc(file)>=beforeUtc) continue;
                if(visited++<offset) continue;result.Add((lead,note,ext));if(result.Count>=100) { SweepOffsets[Root]=visited;return Task.FromResult<IReadOnlyList<(Guid,Guid,string)>>(result); }
            }
        }
        SweepOffsets[Root]=0;return Task.FromResult<IReadOnlyList<(Guid,Guid,string)>>(result);
    }
}
