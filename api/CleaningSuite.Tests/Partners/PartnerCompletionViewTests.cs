using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.QualityCycle;
using System.Text.Json;

namespace CleaningSuite.Tests.Partners;

public class PartnerCompletionViewTests
{
    [Theory]
    [InlineData("test", "T.")]
    [InlineData("  Ada ", "A.")]
    [InlineData("élise", "É.")]
    public void Cleaner_is_masked_to_first_initial(string firstName, string expected)
    {
        Assert.Equal(expected, PartnerCompletionView.MaskCleaner(firstName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cleaner_without_a_recorded_name_is_not_shown(string? firstName)
    {
        Assert.Null(PartnerCompletionView.MaskCleaner(firstName));
    }

    [Fact]
    public void Report_keeps_checklist_state_notes_and_each_photo_with_its_note()
    {
        var form = SubmittedForm("  Bathroom done  ", ("Vacuum floors", true), ("Restock towels", false));
        form.Photos = [new("data:image/png;base64,AAAA", "Kitchen after clean")];

        var report = PartnerCompletionView.Report(form);

        Assert.Equal(form.TemplateTitle, report.TemplateTitle);
        Assert.Equal([new PartnerCompletionTask("Vacuum floors", true), new PartnerCompletionTask("Restock towels", false)], report.Tasks);
        Assert.Equal("Bathroom done", report.CleanerNotes);
        Assert.Equal([new PartnerCompletionPhoto("data:image/png;base64,AAAA", "Kitchen after clean")], report.Photos);
    }

    [Fact]
    public void Blank_notes_are_reported_as_absent()
    {
        var report = PartnerCompletionView.Report(SubmittedForm("   ", ("Vacuum floors", true)));

        Assert.Null(report.CleanerNotes);
    }

    [Fact]
    public void Legacy_photo_urls_without_notes_are_shown_with_an_empty_note()
    {
        var form = SubmittedForm(null, ("Vacuum floors", true));
        form.Photos = [];
        form.PhotoUrls = ["data:image/png;base64,BBBB"];

        var report = PartnerCompletionView.Report(form);

        Assert.Equal([new PartnerCompletionPhoto("data:image/png;base64,BBBB", string.Empty)], report.Photos);
    }

    [Fact]
    public void Partner_payload_carries_only_the_masked_cleaner_identity()
    {
        var dto = new PartnerCompletionDto(Guid.NewGuid(), "Lake apartment", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1),
            null, null, null, null, PartnerCompletionView.MaskCleaner("Test"), [PartnerCompletionView.Report(SubmittedForm(null, ("Vacuum floors", true)))], []);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Contains("\"cleanerInitial\":\"T.\"", json);
        Assert.DoesNotContain("Test Cleaner", json);
        Assert.DoesNotContain("employee", json, StringComparison.OrdinalIgnoreCase);
    }

    private static QualityCycleForm SubmittedForm(string? notes, params (string Item, bool Done)[] tasks)
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Morning clean", Guid.NewGuid(), "Test Cleaner",
            Guid.NewGuid(), "Apartment checklist", new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc), tasks.Select(t => t.Item).ToList());
        form.Start(new DateTime(2026, 12, 1, 8, 5, 0, DateTimeKind.Utc));
        var items = form.Items.Select((item, index) => new QualityCycleFormItem { ItemText = item.ItemText, IsChecked = tasks[index].Done }).ToList();
        form.Submit(items, null, notes, [], new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc));
        return form;
    }
}
