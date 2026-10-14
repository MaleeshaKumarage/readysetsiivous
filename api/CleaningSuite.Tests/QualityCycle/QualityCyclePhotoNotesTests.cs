using System.Text.Json;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.QualityCycle;
using FluentValidation;
using Moq;
using PDFtoImage;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCyclePhotoNotesTests
{
    private static QualityCycleForm Form() => QualityCycleForm.Create(Guid.NewGuid(), "Daily", Guid.NewGuid(),
        "Cleaner identity", Guid.NewGuid(), "Activities", DateTime.UtcNow.AddHours(-1), new() { "Gym" });

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  \n ")]
    public void Every_photo_requires_its_own_note_atomically(string? note)
    {
        var form = Form(); var updated = form.UpdatedUtc;
        Assert.Throws<ArgumentException>(() => form.Submit(new() { new() { ItemText = "Gym", IsChecked = true } }, null,
            "General note cannot replace captions", new() { new("first", "Valid first note"), new("second", note!) }));
        Assert.False(form.IsSubmitted); Assert.False(form.Items[0].IsChecked); Assert.Empty(form.Photos);
        Assert.Null(form.EndedAtUtc); Assert.Equal(updated, form.UpdatedUtc);
    }

    [Fact]
    public void Multiple_notes_stay_paired_and_general_notes_are_optional()
    {
        var form = Form(); form.Submit(new() { new() { ItemText = "Gym" } }, null, null,
            new() { new(" photo-one ", " Corridor cleaned "), new("photo-two", " Gym cleaned ") });
        Assert.Equal(new[] { new QualityCyclePhoto("photo-one", "Corridor cleaned"), new("photo-two", "Gym cleaned") }, form.Photos);
        Assert.Null(form.CleanerNotes); Assert.True(form.IsSubmitted);
        Assert.Equal(form.Photos, QualityCycleHandlers.MapForm(form).Photos);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")]
    public void Empty_photo_url_is_not_silently_dropped(string? url)
    {
        var form = Form(); Assert.Throws<ArgumentException>(() => form.Submit(new() { new() { ItemText = "Gym" } }, null, null,
            new() { new(url!, "Note") })); Assert.False(form.IsSubmitted);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Legacy_url_only_and_ambiguous_payloads_are_rejected(bool paired)
    {
        var form = Form(); Assert.Throws<ArgumentException>(() => form.Submit(new() { new() { ItemText = "Gym" } },
            new() { "legacy-url" }, "General note", paired ? new() { new("new-url", "New note") } : null));
        Assert.False(form.IsSubmitted);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Both_submission_routes_reject_missing_second_photo_note_without_saving(bool authenticated)
    {
        var form = Form(); form.Start(); var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(x => x.GetFormAsync(form.Id, default)).ReturnsAsync(form);
        repository.Setup(x => x.GetFormByTokenAsync(form.Token, default)).ReturnsAsync(form);
        var items = new List<QualityCycleFormItemDto> { new("Gym", true) };
        var photos = new List<QualityCyclePhoto> { new("first", "First note"), new("second", " ") };
        var error = authenticated
            ? await Assert.ThrowsAsync<ValidationException>(() => new QualityCycleHandlers.EndQualityCycleFormCommandHandler(repository.Object).Handle(new(form.Id, form.EmployeeId, items, CleanerNotes: "General note", Photos: photos), default))
            : await Assert.ThrowsAsync<ValidationException>(() => new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(repository.Object).Handle(new(form.Token, items, CleanerNotes: "General note", Photos: photos), default));
        Assert.Equal("Photos", Assert.Single(error.Errors).PropertyName);
        repository.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), default), Times.Never);
    }

    [Fact]
    public async Task Old_summary_only_snapshot_stays_summary_only_and_is_read_without_live_forms()
    {
        var report = new QualityCycleMonthlyReport(2026, 10, "Europe/Helsinki", DateTime.UtcNow,
            new[] { new QualityCycleShiftReport(Guid.NewGuid(), "Daily", Guid.NewGuid(), "AAA", Guid.NewGuid(), "AAA1",
                0, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<QualityCycleOccurrenceReport>(), Array.Empty<QualityCycleFormDto>(), Array.Empty<QualityCycleItemReport>()) });
        var share = new QualityCycleReportShare { ExpiresUtc = DateTime.UtcNow.AddDays(30), ReportJson = JsonSerializer.Serialize(report) };
        var repository = new Mock<IQualityCycleRepository>(); repository.Setup(x => x.GetReportShareByTokenAsync("old", default)).ReturnsAsync(share);
        var pdf = await new GetSharedQualityCycleReportPdfHandler(repository.Object, new QualityCyclePdfGenerator(), TimeProvider.System).Handle(new("old"), default);
        Assert.Equal(1, Conversion.GetPageCount(pdf));
        repository.Verify(x => x.ListFormsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), default), Times.Never);
    }
}
