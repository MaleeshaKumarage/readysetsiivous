using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Partners;

/// <summary>
/// One-time setup for a partner company. Its Id is the company Id. Photo sections and access details apply to
/// every visit the company requests after the setting is saved.
/// </summary>
public sealed class PartnerPortalSettings : BaseDocument
{
    public const int MaxPhotoLabels = 10;
    public const int MaxPhotoLabelLength = 60;
    public const int MaxAccountFieldLength = 300;
    public const int MaxAccessInstructionsLength = 2000;
    public const int MaxGeneralInstructionsLength = 2000;

    public List<string> PhotoLabels { get; set; } = new();
    public PartnerAccountDetails Account { get; set; } = new();
    /// <summary>Instructions for every visit the company requests. Each visit's own instructions are appended after them.</summary>
    public string? GeneralInstructions { get; set; }
}

/// <summary>Billing, contact and access details kept with the partner's visit setup.</summary>
public sealed class PartnerAccountDetails
{
    public string? BillingName { get; set; }
    public string? InvoiceAddress { get; set; }
    public string? BusinessId { get; set; }
    public string? InvoiceReference { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    /// <summary>How the cleaner gets in: key safe code, lockbox, door codes. Shown to the cleaner before the visit.</summary>
    public string? AccessInstructions { get; set; }
}
