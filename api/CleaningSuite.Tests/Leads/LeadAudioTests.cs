using System.Diagnostics;
using CleaningSuite.Infrastructure.Leads;
using FluentValidation;
using Microsoft.Extensions.Configuration;
namespace CleaningSuite.Tests.Leads;
public class LeadAudioTests
{
    [Theory][InlineData("webm","libopus","audio/webm")][InlineData("ogg","libopus","audio/ogg")][InlineData("m4a","aac","audio/mp4")]
    public async Task Real_recorded_audio_formats_are_decoded_and_measured(string extension,string codec,string contentType)
    {
        var bytes=await Audio(extension,codec,1);var result=await new LeadAudioProbe(new ConfigurationBuilder().Build()).ValidateAsync(bytes,default);
        Assert.Equal(contentType,result.ContentType);Assert.InRange(result.DurationSeconds,.9,1.2);
    }
    [Fact] public async Task Invalid_empty_oversized_and_overlong_audio_are_rejected()
    {
        var probe=new LeadAudioProbe(new ConfigurationBuilder().Build());
        foreach(var invalid in new[]{Array.Empty<byte>(),"<svg/>"u8.ToArray(),new byte[20*1024*1024+1]}) await Assert.ThrowsAsync<ValidationException>(()=>probe.ValidateAsync(invalid,default));
        await Assert.ThrowsAsync<ValidationException>(async()=>await probe.ValidateAsync(await Audio("webm","libopus",301),default));
    }
    [Fact] public async Task Browser_mediarecorder_webm_without_duration_is_supported()
    {
        var path=Path.Combine(Path.GetTempPath(),$"lead-fixture-{Guid.NewGuid():N}.webm");
        try {
            await Run(["-v","error","-f","lavfi","-i","sine=frequency=440:duration=1","-c:a","libopus","-f","webm","-live","1",path]);
            var value=await new LeadAudioProbe(new ConfigurationBuilder().Build()).ValidateAsync(await File.ReadAllBytesAsync(path),default);Assert.InRange(value.DurationSeconds,.9,1.2);
        } finally { File.Delete(path); }
    }
    private static async Task<byte[]> Audio(string extension,string codec,int seconds)
    {
        var path=Path.Combine(Path.GetTempPath(),$"lead-fixture-{Guid.NewGuid():N}.{extension}");
        try { await Run(["-v","error","-f","lavfi","-i",$"sine=frequency=440:duration={seconds}","-c:a",codec,path]);return await File.ReadAllBytesAsync(path); } finally { File.Delete(path); }
    }
    private static async Task Run(string[] args)
    {
        var start=new ProcessStartInfo("ffmpeg"){UseShellExecute=false,RedirectStandardError=true,CreateNoWindow=true};foreach(var arg in args) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var error=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Assert.True(process.ExitCode==0,error);
    }
}
