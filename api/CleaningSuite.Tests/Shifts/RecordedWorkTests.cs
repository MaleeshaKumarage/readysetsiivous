using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class RecordedWorkTests
{
    private static readonly DateTime Start = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Employee = Guid.NewGuid();

    private static QualityCycleForm Form(Action<QualityCycleForm>? configure = null)
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Jukankatu", Employee, "Cleaner", Guid.NewGuid(), "Checklist", Start, new() { "Floor" }, Start.AddHours(2));
        configure?.Invoke(form);
        return form;
    }

    [Fact]
    public void Duration_counts_only_submitted_records_with_a_valid_span()
    {
        var submitted = Form(f => { f.Start(Start); f.Submit(new() { new() { ItemText = "Floor", IsChecked = true } }, null, null, null, Start.AddMinutes(80)); });
        Assert.Equal(TimeSpan.FromMinutes(80), RecordedWork.Duration(submitted));

        var unsubmitted = Form(f => { f.StartedAtUtc = Start; f.EndedAtUtc = Start.AddMinutes(30); });
        Assert.Null(RecordedWork.Duration(unsubmitted));

        var inverted = Form(f => { f.IsSubmitted = true; f.StartedAtUtc = Start; f.EndedAtUtc = Start.AddMinutes(-5); });
        Assert.Null(RecordedWork.Duration(inverted));

        var noEnd = Form(f => { f.IsSubmitted = true; f.StartedAtUtc = Start; });
        Assert.Null(RecordedWork.Duration(noEnd));
        Assert.Null(RecordedWork.Duration(null));
    }

    [Fact]
    public void Representative_prefers_a_submitted_record_over_a_later_started_only_record()
    {
        var submitted = Form(f => { f.IsSubmitted = true; f.StartedAtUtc = Start; f.EndedAtUtc = Start.AddMinutes(80); });
        var stray = Form(f => { f.StartedAtUtc = Start.AddHours(1); f.EndedAtUtc = Start.AddHours(1).AddSeconds(36); });

        Assert.Same(submitted, RecordedWork.Representative(new[] { stray, submitted }));
        Assert.Same(submitted, RecordedWork.Representative(new[] { submitted, stray }));
    }

    [Fact]
    public void Representative_orders_by_end_before_start_so_a_later_start_with_an_earlier_end_does_not_win()
    {
        var longerEarlier = Form(f => { f.IsSubmitted = true; f.StartedAtUtc = Start; f.EndedAtUtc = Start.AddMinutes(80); });
        var shorterLater = Form(f => { f.IsSubmitted = true; f.StartedAtUtc = Start.AddMinutes(30); f.EndedAtUtc = Start.AddMinutes(45); });

        Assert.Same(longerEarlier, RecordedWork.Representative(new[] { shorterLater, longerEarlier }));
    }

    [Fact]
    public void Representative_prefers_the_latest_end_among_submitted_records_and_is_null_when_empty()
    {
        var earlier = Form(f => { f.IsSubmitted = true; f.StartedAtUtc = Start; f.EndedAtUtc = Start.AddMinutes(30); });
        var later = Form(f => { f.IsSubmitted = true; f.StartedAtUtc = Start; f.EndedAtUtc = Start.AddMinutes(80); });

        Assert.Same(later, RecordedWork.Representative(new[] { earlier, later }));
        Assert.Null(RecordedWork.Representative(Array.Empty<QualityCycleForm>()));
    }
}
