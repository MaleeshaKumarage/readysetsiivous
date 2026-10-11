using CleaningSuite.Domain.Entertainment;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Entertainment;

public record CreateEntertainmentGroupCommand(
    string Name,
    string GeminiApiKey,
    string Description,
    string CreatorName,
    string CreatorLevel) : IRequest<CreateEntertainmentGroupResult>;

public record CreateEntertainmentGroupResult(
    Guid GroupId,
    string JoinKey,
    Guid CreatorPlayerId);

public class CreateEntertainmentGroupCommandHandler
    : IRequestHandler<CreateEntertainmentGroupCommand, CreateEntertainmentGroupResult>
{
    private readonly IEntertainmentRepository _repository;
    private readonly IEntertainmentKeyProtector _keyProtector;

    public CreateEntertainmentGroupCommandHandler(
        IEntertainmentRepository repository,
        IEntertainmentKeyProtector keyProtector)
    {
        _repository = repository;
        _keyProtector = keyProtector;
    }

    public async Task<CreateEntertainmentGroupResult> Handle(
        CreateEntertainmentGroupCommand request,
        CancellationToken ct)
    {
        var level = ParseLevel(request.CreatorLevel);

        var joinKey = await GenerateUniqueJoinKeyAsync(ct);

        var group = EntertainmentGroup.Create(
            request.Name,
            _keyProtector.Protect(request.GeminiApiKey),
            request.Description,
            joinKey);

        var creator = EntertainmentPlayer.Create(group.Id, request.CreatorName, level, isCreator: true);

        await _repository.CreateGroupAsync(group, creator, ct);

        return new CreateEntertainmentGroupResult(group.Id, joinKey, creator.Id);
    }

    private static CefrLevel ParseLevel(string level)
    {
        if (!Enum.TryParse<CefrLevel>(level, ignoreCase: true, out var parsed))
            throw new ArgumentException($"Invalid level '{level}'.", nameof(level));
        return parsed;
    }

    private async Task<string> GenerateUniqueJoinKeyAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var key = EntertainmentJoinKey.Generate();
            if (!await _repository.JoinKeyExistsAsync(key, ct))
                return key;
        }

        throw new InvalidOperationException("Could not generate a unique join key after 10 attempts.");
    }
}

public class EntertainmentValidators
{
    public class CreateEntertainmentGroupCommandValidator : AbstractValidator<CreateEntertainmentGroupCommand>
    {
        public CreateEntertainmentGroupCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Group name is required.");
            RuleFor(x => x.GeminiApiKey).NotEmpty().WithMessage("Gemini API key is required.");
            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Description is required.")
                .MinimumLength(200).WithMessage("Description must be at least 200 characters.");
            RuleFor(x => x.CreatorName).NotEmpty().WithMessage("Your name is required.");
            RuleFor(x => x.CreatorLevel)
                .Must(IsValidLevel).WithMessage("Level must be one of A1, A2, B1, B2, C1, C2.");
        }

        private static bool IsValidLevel(string level)
            => Enum.TryParse<CefrLevel>(level, ignoreCase: true, out _);
    }
}
