using System.Net;
using CleaningSuite.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleaningSuite.Tests;

public class PublicUploadsTests
{
    [Fact]
    public async Task Static_uploads_serve_service_images_but_never_agreement_documents_or_signatures()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleaning-uploads-" + Guid.NewGuid().ToString("N"));
        var agreementDirectory = Path.Combine(root, "agreements", "tenant", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(agreementDirectory);
        var image = Guid.NewGuid().ToString("N") + ".png";
        var signature = Guid.NewGuid().ToString("N") + ".png";
        await File.WriteAllTextAsync(Path.Combine(root, image), "public image");
        await File.WriteAllTextAsync(Path.Combine(agreementDirectory, "original.pdf"), "private original");
        await File.WriteAllTextAsync(Path.Combine(agreementDirectory, "signed.pdf"), "private signed");
        await File.WriteAllTextAsync(Path.Combine(agreementDirectory, signature), "private signature");
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Uploads:Path"] = root }));
                builder.ConfigureServices(services =>
                {
                    var initializer = services.Single(x => x.ImplementationType == typeof(MartenSchemaInitializer));
                    services.Remove(initializer);
                });
            });
            using var client = factory.CreateClient();
            var publicResponse = await client.GetAsync("/uploads/" + image);
            Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
            Assert.Equal("public image", await publicResponse.Content.ReadAsStringAsync());
            var relativeDirectory = Path.GetRelativePath(root, agreementDirectory).Replace('\\', '/');
            foreach (var file in new[] { "original.pdf", "signed.pdf", signature })
            {
                var response = await client.GetAsync($"/uploads/{relativeDirectory}/{file}");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
            // Private controller downloads still use the unchanged persisted paths.
            Assert.True(File.Exists(Path.Combine(agreementDirectory, "original.pdf")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("agreements/tenant/id/original.pdf")]
    [InlineData("agreements/tenant/id/0123456789abcdef0123456789abcdef.png")]
    [InlineData("../0123456789abcdef0123456789abcdef.png")]
    [InlineData("agreements\\tenant\\id\\0123456789abcdef0123456789abcdef.png")]
    [InlineData("0123456789abcdef0123456789abcdef.pdf")]
    public void File_provider_rejects_private_paths(string path)
    {
        using var provider = new PublicUploadsFileProvider(Path.GetTempPath());
        Assert.False(provider.GetFileInfo(path).Exists);
    }
}
