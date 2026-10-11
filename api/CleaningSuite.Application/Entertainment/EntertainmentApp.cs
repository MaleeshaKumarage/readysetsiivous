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

public record JoinEntertainmentGroupCommand(
    string JoinKey,
    string PlayerName,
    string Level) : IRequest<JoinEntertainmentGroupResult>;

public record JoinEntertainmentGroupResult(
    Guid GroupId,
    Guid PlayerId,
    string PlayerName,
    string Level);

public record EntertainmentLobbyPlayerDto(
    Guid PlayerId,
    string Name,
    string Level,
    bool IsCreator,
    bool IsOnline);

public record EntertainmentLobbyDto(
    Guid GroupId,
    string GroupName,
    string Status,
    IReadOnlyList<EntertainmentLobbyPlayerDto> Players);

public record GetEntertainmentLobbyQuery(Guid GroupId) : IRequest<EntertainmentLobbyDto>;

public class JoinEntertainmentGroupCommandHandler
    : IRequestHandler<JoinEntertainmentGroupCommand, JoinEntertainmentGroupResult>
{
    private readonly IEntertainmentRepository _repository;

    public JoinEntertainmentGroupCommandHandler(IEntertainmentRepository repository) => _repository = repository;

    public async Task<JoinEntertainmentGroupResult> Handle(
        JoinEntertainmentGroupCommand request,
        CancellationToken ct)
    {
        var group = await _repository.GetGroupByJoinKeyAsync(request.JoinKey, ct)
            ?? throw new EntertainmentGroupNotFoundException(request.JoinKey);

        if (group.Status != EntertainmentStatus.Lobby)
            throw new EntertainmentJoinClosedException(request.JoinKey);

        var level = ParseLevel(request.Level);
        var player = EntertainmentPlayer.Create(group.Id, request.PlayerName, level, isCreator: false);

        await _repository.SavePlayerAsync(player, ct);

        return new JoinEntertainmentGroupResult(
            group.Id,
            player.Id,
            player.Name,
            player.Level.ToString());
    }

    private static CefrLevel ParseLevel(string level)
    {
        if (!Enum.TryParse<CefrLevel>(level, ignoreCase: true, out var parsed))
            throw new ArgumentException($"Invalid level '{level}'.", nameof(level));
        return parsed;
    }
}

public class GetEntertainmentLobbyQueryHandler : IRequestHandler<GetEntertainmentLobbyQuery, EntertainmentLobbyDto>
{
    private readonly IEntertainmentRepository _repository;

    public GetEntertainmentLobbyQueryHandler(IEntertainmentRepository repository) => _repository = repository;

    public async Task<EntertainmentLobbyDto> Handle(
        GetEntertainmentLobbyQuery request,
        CancellationToken ct)
    {
        var group = await _repository.GetGroupAsync(request.GroupId, ct)
            ?? throw new EntertainmentGroupNotFoundException(request.GroupId.ToString());

        var players = await _repository.ListPlayersAsync(request.GroupId, ct);

        var dtos = players
            .Select(p => new EntertainmentLobbyPlayerDto(
                p.Id, p.Name, p.Level.ToString(), p.IsCreator, IsOnline: true))
            .ToList();

        return new EntertainmentLobbyDto(
            group.Id, group.Name, group.Status.ToString(), dtos);
    }
}

public record StartEntertainmentGameCommand(
    Guid GroupId,
    Guid PlayerId) : IRequest<StartEntertainmentGameResult>;

public record EntertainmentRoundDto(
    Guid RoundId,
    int RoundNumber,
    Guid TranslatorPlayerId,
    string SentenceEn,
    string? SentenceFi,
    IReadOnlyList<string> BaseForms);

public record StartEntertainmentGameResult(
    Guid GroupId,
    EntertainmentRoundDto Round,
    IReadOnlyList<Guid> TurnOrder);

public class StartEntertainmentGameCommandHandler
    : IRequestHandler<StartEntertainmentGameCommand, StartEntertainmentGameResult>
{
    private readonly IEntertainmentRepository _repository;
    private readonly IEntertainmentKeyProtector _keyProtector;
    private readonly IGeminiSentenceService _gemini;

    public StartEntertainmentGameCommandHandler(
        IEntertainmentRepository repository,
        IEntertainmentKeyProtector keyProtector,
        IGeminiSentenceService gemini)
    {
        _repository = repository;
        _keyProtector = keyProtector;
        _gemini = gemini;
    }

    public async Task<StartEntertainmentGameResult> Handle(
        StartEntertainmentGameCommand request,
        CancellationToken ct)
    {
        var group = await _repository.GetGroupAsync(request.GroupId, ct)
            ?? throw new EntertainmentGroupNotFoundException(request.GroupId.ToString());

        if (group.Status != EntertainmentStatus.Lobby)
            throw new EntertainmentGameAlreadyStartedException(group.Id);

        var creator = await _repository.GetPlayerAsync(request.PlayerId, ct);
        if (creator is null || !creator.IsCreator || creator.GroupId != group.Id)
            throw new UnauthorizedAccessException("Only the group creator can start the game.");

        var players = await _repository.ListPlayersAsync(group.Id, ct);
        var turnOrder = players.Select(p => p.Id).ToList();
        if (turnOrder.Count == 0)
            throw new InvalidOperationException("No players in group.");

        var translator = players[0];
        var apiKey = _keyProtector.Unprotect(group.GeminiApiKeyEncrypted);
        var sentence = await _gemini.GenerateAsync(group.Description, translator.Level, apiKey, ct);

        var round = EntertainmentRound.Create(
            group.Id, 1, translator.Id, sentence.SentenceEn, sentence.SentenceFi, sentence.BaseForms);

        group.Status = EntertainmentStatus.InGame;
        group.UpdatedUtc = DateTime.UtcNow;

        await _repository.SaveGroupAsync(group, ct);
        await _repository.SaveRoundAsync(round, ct);

        return new StartEntertainmentGameResult(
            group.Id,
            new EntertainmentRoundDto(round.Id, round.RoundNumber, round.TranslatorPlayerId, round.SentenceEn, round.SentenceFi, round.BaseForms),
            turnOrder);
    }
}

public record StartNextEntertainmentRoundCommand(
    Guid GroupId,
    Guid TranslatorPlayerId,
    int RoundNumber) : IRequest<EntertainmentRoundDto>;

public record FinalizeEntertainmentRoundCommand(
    Guid GroupId,
    Guid RoundId,
    IReadOnlyDictionary<Guid, int> Scores) : IRequest;

public record EntertainmentStandingDto(
    Guid PlayerId,
    string Name,
    int Score,
    bool IsCreator);

public record EndEntertainmentGameCommand(
    Guid GroupId,
    Guid PlayerId,
    IReadOnlyDictionary<Guid, int> Scores) : IRequest<IReadOnlyList<EntertainmentStandingDto>>;

public class StartNextEntertainmentRoundCommandHandler
    : IRequestHandler<StartNextEntertainmentRoundCommand, EntertainmentRoundDto>
{
    private readonly IEntertainmentRepository _repository;
    private readonly IEntertainmentKeyProtector _keyProtector;
    private readonly IGeminiSentenceService _gemini;

    public StartNextEntertainmentRoundCommandHandler(
        IEntertainmentRepository repository,
        IEntertainmentKeyProtector keyProtector,
        IGeminiSentenceService gemini)
    {
        _repository = repository;
        _keyProtector = keyProtector;
        _gemini = gemini;
    }

    public async Task<EntertainmentRoundDto> Handle(
        StartNextEntertainmentRoundCommand request,
        CancellationToken ct)
    {
        var group = await _repository.GetGroupAsync(request.GroupId, ct)
            ?? throw new EntertainmentGroupNotFoundException(request.GroupId.ToString());

        var translator = await _repository.GetPlayerAsync(request.TranslatorPlayerId, ct)
            ?? throw new EntertainmentGroupNotFoundException(request.TranslatorPlayerId.ToString());

        var apiKey = _keyProtector.Unprotect(group.GeminiApiKeyEncrypted);
        var sentence = await _gemini.GenerateAsync(group.Description, translator.Level, apiKey, ct);

        var round = EntertainmentRound.Create(
            group.Id, request.RoundNumber, translator.Id, sentence.SentenceEn, sentence.SentenceFi, sentence.BaseForms);

        await _repository.SaveRoundAsync(round, ct);

        return new EntertainmentRoundDto(round.Id, round.RoundNumber, round.TranslatorPlayerId, round.SentenceEn, round.SentenceFi, round.BaseForms);
    }
}

public class FinalizeEntertainmentRoundCommandHandler : IRequestHandler<FinalizeEntertainmentRoundCommand>
{
    private readonly IEntertainmentRepository _repository;

    public FinalizeEntertainmentRoundCommandHandler(IEntertainmentRepository repository) => _repository = repository;

    public async Task Handle(FinalizeEntertainmentRoundCommand request, CancellationToken ct)
    {
        var round = await _repository.GetRoundAsync(request.RoundId, ct);
        if (round is null)
            return;

        round.EndedUtc = DateTime.UtcNow;
        round.FinalScores = new Dictionary<Guid, int>(request.Scores);
        round.UpdatedUtc = DateTime.UtcNow;

        await _repository.SaveRoundAsync(round, ct);
    }
}

public class EndEntertainmentGameCommandHandler
    : IRequestHandler<EndEntertainmentGameCommand, IReadOnlyList<EntertainmentStandingDto>>
{
    private readonly IEntertainmentRepository _repository;

    public EndEntertainmentGameCommandHandler(IEntertainmentRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<EntertainmentStandingDto>> Handle(
        EndEntertainmentGameCommand request,
        CancellationToken ct)
    {
        var group = await _repository.GetGroupAsync(request.GroupId, ct)
            ?? throw new EntertainmentGroupNotFoundException(request.GroupId.ToString());

        var creator = await _repository.GetPlayerAsync(request.PlayerId, ct);
        if (creator is null || !creator.IsCreator || creator.GroupId != group.Id)
            throw new UnauthorizedAccessException("Only the group creator can end the game.");

        var players = await _repository.ListPlayersAsync(group.Id, ct);

        foreach (var player in players)
        {
            player.Score = request.Scores.GetValueOrDefault(player.Id);
            player.UpdatedUtc = DateTime.UtcNow;
            await _repository.SavePlayerAsync(player, ct);
        }

        group.Status = EntertainmentStatus.Finished;
        group.UpdatedUtc = DateTime.UtcNow;
        await _repository.SaveGroupAsync(group, ct);

        return players
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.JoinedUtc)
            .Select(p => new EntertainmentStandingDto(p.Id, p.Name, p.Score, p.IsCreator))
            .ToList();
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

    public class JoinEntertainmentGroupCommandValidator : AbstractValidator<JoinEntertainmentGroupCommand>
    {
        public JoinEntertainmentGroupCommandValidator()
        {
            RuleFor(x => x.JoinKey).NotEmpty().WithMessage("Join key is required.");
            RuleFor(x => x.PlayerName).NotEmpty().WithMessage("Your name is required.");
            RuleFor(x => x.Level)
                .Must(IsValidLevel).WithMessage("Level must be one of A1, A2, B1, B2, C1, C2.");
        }

        private static bool IsValidLevel(string level)
            => Enum.TryParse<CefrLevel>(level, ignoreCase: true, out _);
    }
}
