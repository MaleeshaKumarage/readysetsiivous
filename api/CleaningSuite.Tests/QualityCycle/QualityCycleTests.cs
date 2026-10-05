using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleTests
{
    [Fact]
    public void CreateQualityCycleTemplate_WithValidInput_Succeeds()
    {
        var items = new List<string> { "Vacuum floor", "Wipe surfaces", "Empty trash" };
        var template = QualityCycleTemplate.Create("Daily Office Checklist", items, "Standard daily cleaning");

        Assert.NotNull(template);
        Assert.Equal("Daily Office Checklist", template.Title);
        Assert.Equal("Standard daily cleaning", template.Description);
        Assert.Equal(3, template.Items.Count);
        Assert.True(template.IsActive);
    }

    [Fact]
    public void CreateQualityCycleTemplate_WithEmptyItems_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QualityCycleTemplate.Create("Title", new List<string>()));
    }

    [Fact]
    public void SubmitQualityCycleForm_UpdatesFormStateAndIsSubmitted()
    {
        var templateItems = new List<string> { "Task 1", "Task 2" };
        var form = QualityCycleForm.Create(
            Guid.NewGuid(),
            "Morning Shift",
            Guid.NewGuid(),
            "John Cleaner",
            Guid.NewGuid(),
            "Daily Template",
            DateTime.UtcNow,
            templateItems);

        Assert.False(form.IsSubmitted);

        var submissionItems = new List<QualityCycleFormItem>
        {
            new QualityCycleFormItem { ItemText = "Task 1", IsChecked = true },
            new QualityCycleFormItem { ItemText = "Task 2", IsChecked = false }
        };

        form.Submit(submissionItems, new List<string> { "https://example.com/photo1.jpg" }, "All clear");

        Assert.True(form.IsSubmitted);
        Assert.NotNull(form.SubmittedUtc);
        Assert.Equal("All clear", form.CleanerNotes);
        Assert.Single(form.PhotoUrls);
        Assert.True(form.Items[0].IsChecked);
        Assert.False(form.Items[1].IsChecked);
    }

    [Fact]
    public async Task CreateQualityCycleTemplateCommandHandler_SavesAndReturnsDto()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var handler = new QualityCycleHandlers.CreateQualityCycleTemplateCommandHandler(repoMock.Object);

        var command = new CreateQualityCycleTemplateCommand(
            "Test Template",
            new List<string> { "Item 1", "Item 2" },
            "Description");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test Template", result.Title);
        Assert.Equal(2, result.Items.Count);
        repoMock.Verify(x => x.SaveTemplateAsync(It.IsAny<QualityCycleTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitQualityCycleFormCommandHandler_UpdatesAndSavesForm()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var form = QualityCycleForm.Create(
            Guid.NewGuid(), "Shift 1", Guid.NewGuid(), "Cleaner 1", Guid.NewGuid(), "Template 1", DateTime.UtcNow, new List<string> { "Item 1" });

        repoMock.Setup(x => x.GetFormByTokenAsync(form.Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(form);

        var handler = new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(repoMock.Object);

        var command = new SubmitQualityCycleFormCommand(
            form.Token,
            new List<QualityCycleFormItemDto> { new QualityCycleFormItemDto("Item 1", true) },
            new List<string> { "data:image/png;base64,123" },
            "Done!");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsSubmitted);
        Assert.Equal("Done!", result.CleanerNotes);
        repoMock.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void QuestPdfGenerator_GeneratesNonEmptyByteArray()
    {
        var pdfGen = new Infrastructure.QualityCycle.QualityCyclePdfGenerator();
        var form = new QualityCycleFormDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Daily Office Shift",
            Guid.NewGuid(),
            "Alex Smith",
            Guid.NewGuid(),
            "Office Checklist",
            DateTime.UtcNow,
            "token123",
            new List<QualityCycleFormItemDto> { new QualityCycleFormItemDto("Wipe desks", true) },
            new List<string>(),
            "Cleaned thoroughly",
            true,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var pdfBytes = pdfGen.GenerateMonthlySummaryPdf(
            "Daily Office Shift",
            "Acme Corp",
            "Main Branch",
            2025,
            10,
            new List<QualityCycleFormDto> { form });

        Assert.NotNull(pdfBytes);
        Assert.NotEmpty(pdfBytes);
    }
}
