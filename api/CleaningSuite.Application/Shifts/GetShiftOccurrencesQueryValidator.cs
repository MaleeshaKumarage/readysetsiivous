using FluentValidation;

namespace CleaningSuite.Application.Shifts;

public class GetShiftOccurrencesQueryValidator : AbstractValidator<GetShiftOccurrencesQuery>
{
    // Cap the materialized window so a single request cannot expand a shift into
    // tens of thousands of occurrences.
    private const int MaxWindowDays = 366;

    public GetShiftOccurrencesQueryValidator()
    {
        RuleFor(x => x.To)
            .GreaterThan(x => x.From)
            .WithMessage("'to' must be later than 'from'.");

        RuleFor(x => x.To)
            .Must((query, to) => (to - query.From).TotalDays <= MaxWindowDays)
            .WithMessage($"The requested span must not exceed {MaxWindowDays} days.");
    }
}
