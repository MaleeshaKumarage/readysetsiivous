using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace CleaningSuite.Api;

/// <summary>Only service card images are public. Agreement files share the volume,
/// but must be retrieved through the agreement controllers' access checks.</summary>
public sealed class PublicUploadsFileProvider : IFileProvider, IDisposable
{
    private static readonly Regex ImagePath = new(
        @"^/?[a-fA-F0-9]{32}\.(jpg|jpeg|png|webp)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly PhysicalFileProvider _files;

    public PublicUploadsFileProvider(string root) => _files = new PhysicalFileProvider(root);

    public IFileInfo GetFileInfo(string subpath) => ImagePath.IsMatch(subpath)
        ? _files.GetFileInfo(subpath)
        : new NotFoundFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    public void Dispose() => _files.Dispose();
}
