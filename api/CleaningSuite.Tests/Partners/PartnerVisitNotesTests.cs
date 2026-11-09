using CleaningSuite.Application.Partners;

namespace CleaningSuite.Tests.Partners;

public class PartnerVisitNotesTests
{
    [Fact]
    public void General_instructions_come_first_and_the_visit_instructions_are_appended()
    {
        Assert.Equal("Use the side door.\n\nAfter guest checkout.", PartnerService.VisitNotes("Use the side door.", "After guest checkout."));
    }

    [Fact]
    public void A_visit_without_its_own_instructions_shows_only_the_general_ones()
    {
        Assert.Equal("Use the side door.", PartnerService.VisitNotes("Use the side door.", "   "));
    }

    [Fact]
    public void A_visit_without_general_instructions_shows_only_its_own()
    {
        Assert.Equal("After guest checkout.", PartnerService.VisitNotes(null, " After guest checkout. "));
    }

    [Fact]
    public void No_instructions_at_all_leaves_the_visit_without_notes()
    {
        Assert.Null(PartnerService.VisitNotes("", null));
    }
}
