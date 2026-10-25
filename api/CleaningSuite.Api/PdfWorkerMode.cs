using System.Diagnostics;
using System.Text.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Infrastructure.Agreements;

namespace CleaningSuite.Api;

public static class PdfWorkerMode
{
    private const int MaximumRequest = 190 * 1024 * 1024;
    public static async Task<int> RunTaskAsync(string directory)
    {
        // Parent passes only its private job directory. No configuration or .env load.
        try
        {
            await using var input = File.OpenRead(Path.Combine(directory, "request.json"));
            var bytes = await BoundedStreamReader.ReadAsync(input, MaximumRequest, "PDF request", CancellationToken.None);
            if (bytes.Length > MaximumRequest) return 2;
            var request = JsonSerializer.Deserialize<PdfWorkerRequest>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 8, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            if (request is null || request.Operation is not ("inspect" or "inspect-generated" or "sign")) return 2;
            PdfWorkerClient.CheckEnvelope(request.Pdf, request.Operation == "inspect-generated" ? 100 * 1024 * 1024 : 20 * 1024 * 1024);
            PdfRasterBudget.Inspect(request.Pdf);
            if (request.Operation != "sign") return 0;
            if (request.Signers is null || request.Signers.Length is < 1 or > 20) return 2;
            var signers = new List<Signer>();
            for (var i = 0; i < request.Signers.Length; i++)
            {
                var item = request.Signers[i];
                if (item is null || item.Name is null || item.Name.Length > 200 || item.TypedName?.Length > 200) return 2;
                string? signature = null;
                if (item.Image is { } image)
                {
                    new CleaningSuite.Infrastructure.Uploads.UploadContentValidator().ValidateSignaturePng(image);
                    signature = Path.Combine(directory, $"signature-{i}.png");
                    await File.WriteAllBytesAsync(signature, image);
                }
                signers.Add(new Signer { Name = item.Name, TypedName = item.TypedName, SignedAtUtc = item.SignedAtUtc, SignatureImagePath = signature });
            }
            await File.WriteAllBytesAsync(Path.Combine(directory, "input.pdf"), request.Pdf);
            var output = Path.Combine(directory, "output.pdf");
            await new NativeAgreementPdfSigner().GenerateSignedPdfAsync(Path.Combine(directory, "input.pdf"), signers, output);
            await using var generated = File.OpenRead(output);
            var result = await BoundedStreamReader.ReadAsync(generated, 100 * 1024 * 1024, "Signed PDF", CancellationToken.None);
            PdfWorkerClient.CheckEnvelope(result, 100 * 1024 * 1024);
            PdfRasterBudget.Inspect(result);
            return 0;
        }
        catch { return 2; }
    }

    public static async Task RunHostAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaximumRequest);
        var app = builder.Build();
        var admission = new SemaphoreSlim(1, 1);
        app.MapGet("/healthz", () => Results.Ok());
        app.MapPost("/process", async (HttpContext context) =>
        {
            if (!await admission.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            var directory = Path.Combine(Path.GetTempPath(), "pdf-job-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                var seconds = int.TryParse(Environment.GetEnvironmentVariable("PDF_WORKER_DEADLINE_SECONDS"), out var configured) ? Math.Clamp(configured, 1, 40) : 40;
                deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
                var bytes = await BoundedStreamReader.ReadAsync(context.Request.Body, MaximumRequest, "PDF request", deadline.Token);
                await File.WriteAllBytesAsync(Path.Combine(directory, "request.json"), bytes, deadline.Token);
                // Only the child deserializes attacker-controlled JSON/native input.
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(typeof(PdfWorkerMode).Assembly.Location);
                start.ArgumentList.Add("--pdf-worker-task"); start.ArgumentList.Add(directory);
                start.Environment.Clear();
                start.Environment["DOTNET_ROOT"] = "/usr/share/dotnet";
                start.Environment["DOTNET_EnableDiagnostics"] = "0";
                start.Environment["TMPDIR"] = directory;
                foreach (var name in new[] { "PDF_RASTER_DPI", "PDF_RASTER_ENCODED_BYTES", "PDF_RASTER_MAX_DIMENSION", "PDF_RASTER_PAGE_PIXELS", "PDF_RASTER_TOTAL_PIXELS" })
                    if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
                using var child = Process.Start(start) ?? throw new InvalidOperationException();
                try
                {
                    var stdout = child.StandardOutput.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
                    var stderr = child.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
                    await child.WaitForExitAsync(deadline.Token); await Task.WhenAll(stdout, stderr);
                    if (child.ExitCode != 0) { context.Response.StatusCode = 422; return; }
                    var output = Path.Combine(directory, "output.pdf");
                    if (File.Exists(output))
                    {
                        await using var file = File.OpenRead(output);
                        if (file.Length > 100 * 1024 * 1024) { context.Response.StatusCode = 422; return; }
                        context.Response.ContentType = "application/pdf";
                        await file.CopyToAsync(context.Response.Body, deadline.Token);
                    }
                    else context.Response.StatusCode = 204;
                }
                finally
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); }
                }
            }
            catch (OperationCanceledException) { if (!context.Response.HasStarted) context.Response.StatusCode = 408; }
            catch { if (!context.Response.HasStarted) context.Response.StatusCode = 422; }
            finally
            {
                // A failed cleanup must not admit more jobs into a contaminated
                // workspace. Restart the isolated service to recover fail-closed.
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                admission.Release();
            }
        });
        await app.RunAsync();
    }
}
