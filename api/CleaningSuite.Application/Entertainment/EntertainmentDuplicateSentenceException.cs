namespace CleaningSuite.Application.Entertainment;

public sealed class EntertainmentDuplicateSentenceException : Exception
{
    public EntertainmentDuplicateSentenceException() : base("That question was already used. Please retry.") { }
}
