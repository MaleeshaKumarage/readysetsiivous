using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Tests.Partners;

public class PartnerPhotoSectionsTests
{
    [Fact]
    public void Every_required_section_needs_at_least_one_photo_answering_it()
    {
        var missing = QualityCycleHandlers.EndQualityCycleFormCommandHandler.MissingRequiredPhotos(
            ["Before", "After", "Keys"],
            [new("data:image/png;base64,AAAA", "Hallway", " before "), new("data:image/png;base64,BBBB", "Free photo")]);

        Assert.Equal(new[] { "After", "Keys" }, missing);
    }

    [Fact]
    public void A_visit_without_required_sections_asks_for_nothing()
    {
        Assert.Empty(QualityCycleHandlers.EndQualityCycleFormCommandHandler.MissingRequiredPhotos([], null));
    }

    [Fact]
    public void Photos_without_a_section_do_not_satisfy_a_required_one()
    {
        var missing = QualityCycleHandlers.EndQualityCycleFormCommandHandler.MissingRequiredPhotos(["Keys"], [new("data:image/png;base64,AAAA", "Keys on the table")]);

        Assert.Equal(new[] { "Keys" }, missing);
    }
}
