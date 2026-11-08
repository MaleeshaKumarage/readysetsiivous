using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Partners;

/// <summary>
/// One-time setup for a partner company. Its Id is the company Id. Applies to every visit the company requests
/// after the setting is saved.
/// </summary>
public sealed class PartnerPortalSettings : BaseDocument
{
    public const int MaxPhotoLabels = 10;
    public const int MaxPhotoLabelLength = 60;

    public List<string> PhotoLabels { get; set; } = new();
}
