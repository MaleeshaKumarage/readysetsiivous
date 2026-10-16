namespace CleaningSuite.Domain.Entertainment;

public class EntertainmentSentenceReservation
{
    public string Id { get; set; } = string.Empty;
    public Guid GroupId { get; set; }
    public Guid RoundId { get; set; }
}
