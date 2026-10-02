using CleaningSuite.Infrastructure.Agreements;
using System.IO;
using System.Threading.Tasks;
using Xunit;

public class DiskAgreementFileStoreTests
{
    private readonly string _uploadsPath;

    public DiskAgreementFileStoreTests()
    {
        _uploadsPath = Path.Combine(Path.GetTempPath(), "DiskAgreementFileStoreTests");
        Directory.CreateDirectory(_uploadsPath);
    }

    [Fact]
    public async Task Test_SaveAndLoadFile()
    {
        var fileStore = new DiskAgreementFileStore(_uploadsPath);
        var content = "Test content";
        var filePath = Path.Combine(_uploadsPath, "test.txt");

        await fileStore.SaveAsync("test.txt", content);
        var loadedContent = await fileStore.LoadAsync("test.txt");

        Assert.Equal(content, loadedContent);
    }

    [Fact]
    public async Task Test_FileNotFound()
    {
        var fileStore = new DiskAgreementFileStore(_uploadsPath);
        var filePath = Path.Combine(_uploadsPath, "nonexistent.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(() => fileStore.LoadAsync("nonexistent.txt"));
    }

    public void Dispose()
    {
        Directory.Delete(_uploadsPath, true);
    }
}
