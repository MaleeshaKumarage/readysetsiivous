using System.Net.Http.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Agreements;
using FluentValidation;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Agreements;

public record PdfWorkerSigner(string Name, string? TypedName, DateTime? SignedAtUtc, byte[]? Image);
public record PdfWorkerRequest(string Operation, byte[] Pdf, PdfWorkerSigner[] Signers);

public interface IPdfWorkerClient
{
    Task InspectAsync(ReadOnlyMemory<byte> content, bool generated, CancellationToken ct);
    Task SignAsync(string original, IReadOnlyList<Signer> signers, string output, CancellationToken ct);
}

// One shared admission gate bounds retained input across all request-scoped callers.
public sealed class PdfWorkerClient(IConfiguration configuration) : IPdfWorkerClient
{
    private readonly SemaphoreSlim _admission = new(1, 1);
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
    { Timeout = Timeout.InfiniteTimeSpan };

    public async Task InspectAsync(ReadOnlyMemory<byte> content, bool generated, CancellationToken ct)
    {
        CheckEnvelope(content.Span, generated ? 100 * 1024 * 1024 : 20 * 1024 * 1024);
        await WithAdmissionAsync(() => ExecuteAsync(new(generated ? "inspect-generated" : "inspect", content.ToArray(), []), null, ct), ct);
    }

    public async Task SignAsync(string original, IReadOnlyList<Signer> signers, string output, CancellationToken ct)
    {
        await WithAdmissionAsync(async () =>
        {
            if (signers.Count is < 1 or > 20) throw new ValidationException("Unsupported number of agreement signatures.");
            await using var input = File.OpenRead(original);
            var pdf = await BoundedStreamReader.ReadAsync(input, 20 * 1024 * 1024, "Agreement PDF", ct);
            CheckEnvelope(pdf, 20 * 1024 * 1024);
            var snapshots = new List<PdfWorkerSigner>();
            foreach (var signer in signers)
            {
                byte[]? image = null;
                if (signer.SignatureImagePath is { } path)
                {
                    await using var signature = File.OpenRead(path);
                    image = await BoundedStreamReader.ReadAsync(signature, 2 * 1024 * 1024, "Signature", ct);
                }
                snapshots.Add(new(signer.Name, signer.TypedName, signer.SignedAtUtc, image));
            }
            await ExecuteAsync(new("sign", pdf, snapshots.ToArray()), output, ct);
        }, ct);
    }

    private async Task WithAdmissionAsync(Func<Task> operation, CancellationToken ct)
    {
        if (!await _admission.WaitAsync(0, ct))
            throw new ValidationException("Document processing is busy. Please retry.");
        try { await operation(); }
        finally { _admission.Release(); }
    }

    private async Task ExecuteAsync(PdfWorkerRequest request, string? output, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(50));
        try
        {
            var endpoint = configuration["PdfWorker:Url"];
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.AbsolutePath != "/process")
                throw new ValidationException("Document processing is unavailable. Ask an administrator to configure the isolated PDF worker.");
            using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(request) };
            using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw new ValidationException("Document processing failed or exceeded supported limits. Please retry with a supported PDF.");
            if (output is null) return;
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            var bytes = await BoundedStreamReader.ReadAsync(body, 100 * 1024 * 1024, "Signed PDF", deadline.Token);
            CheckEnvelope(bytes, 100 * 1024 * 1024);
            // Caller owns an immutable attempt path. Never overwrite an existing artifact.
            await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await destination.WriteAsync(bytes, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ValidationException("Document processing timed out. Please retry."); }
        catch (HttpRequestException)
        { throw new ValidationException("Document processing is unavailable. Please retry."); }
    }

    public static void CheckEnvelope(ReadOnlySpan<byte> content, int maximum)
    {
        if (content.Length > maximum) throw new UploadPayloadTooLargeException("PDF exceeds the supported size limit.");
        if (content.Length < 5 || !content[..5].SequenceEqual("%PDF-"u8)) throw new ValidationException("Choose a valid non-empty PDF document.");
    }
}
