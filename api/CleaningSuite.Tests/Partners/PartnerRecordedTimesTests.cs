using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Tests.Partners;

public class PartnerRecordedTimesTests
{
    private static readonly DateTime Start = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Employee = Guid.NewGuid();

    private static QualityCycleForm Submitted(DateTime started, DateTime ended)
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Jukankatu", Employee, "Cleaner", Guid.NewGuid(), "Checklist", Start, new() { "Floor" }, Start.AddHours(2));
        form.IsSubmitted = true; form.StartedAtUtc = started; form.EndedAtUtc = ended;
        return form;
    }

    [Fact]
    public void Partner_times_match_the_calendar_representative_record()
    {
        // One cleaner, two submitted records: an 80-minute visit, then a 36-second record that ends later (#368).
        var full = Submitted(Start, Start.AddMinutes(80));
        var brief = Submitted(Start.AddMinutes(85), Start.AddMinutes(85).AddSeconds(36));
        var forms = new List<QualityCycleForm> { full, brief };

        var (started, ended) = CleaningSuite.Application.Partners.PartnerService.RecordedTimes(forms);
        var calendar = RecordedWork.Duration(RecordedWork.Representative(forms));

        Assert.Equal(brief.StartedAtUtc, started);
        Assert.Equal(brief.EndedAtUtc, ended);
        Assert.Equal(calendar, ended - started);
        Assert.Equal(TimeSpan.FromSeconds(36), calendar);
    }

    [Fact]
    public void No_recorded_forms_gives_no_times()
    {
        Assert.Equal((null, null), CleaningSuite.Application.Partners.PartnerService.RecordedTimes([]));
    }
}
