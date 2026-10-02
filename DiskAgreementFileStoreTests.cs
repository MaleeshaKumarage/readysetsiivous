using CleaningSuite.Infrastructure.Agreements;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

public class DiskAgreementFileStoreTests : IDisposable
{
    private readonly string _uploadsPath;

    public DiskAgreementFileStoreTests()
    {
        // Unique per-instance directory so parallel test runs and reruns can't collide.
        _uploadsPath = Path.Combine(Path.GetTempPath(), "DiskAgreementFileStoreTests", Path.GetRandomFileName());
        Directory.CreateDirectory(_uploadsPath);
    }

    [Fact]
    public async Task Test_SaveAndLoadFile()
    {
        var fileStore = new DiskAgreementFileStore(_uploadsPath);
        var content = "Test content";

        await fileStore.SaveAsync("test.txt", content);
        var loadedContent = await fileStore.LoadAsync("test.txt");

        Assert.Equal(content, loadedContent);
    }

    [Fact]
    public async Task Test_FileNotFound()
    {
        var fileStore = new DiskAgreementFileStore(_uploadsPath);

        await Assert.ThrowsAsync<FileNotFoundException>(() => fileStore.LoadAsync("nonexistent.txt"));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_uploadsPath))
        {
            return;
        }

        try
        {
            Directory.Delete(_uploadsPath, true);
        }
        catch (IOException)
        {
            // A file handle may still be held (e.g. by DiskAgreementFileStore).
            // Cleanup failure must not fail otherwise-passing tests.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above: best-effort cleanup only.
        }
    }
}
