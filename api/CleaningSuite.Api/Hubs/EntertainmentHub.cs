using CleaningSuite.Application.Entertainment;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace CleaningSuite.Api.Hubs;

/// <summary>
/// Realtime channel for the entertainment game. Handles the lobby, the start of
/// the game, per-round reveal/done/skip events, scoring, and rotation. Round
/// payloads are pushed per-role (translator vs. observers).
/// </summary>
public class EntertainmentHub : Hub
{
    private readonly IMediator _mediator;
    private readonly IEntertainmentRepository _repository;
    private readonly GameSessionManager _sessions;

    public EntertainmentHub(
        IMediator mediator,
        IEntertainmentRepository repository,
        GameSessionManager sessions)
    {
        _mediator = mediator;
        _repository = repository;
        _sessions = sessions;
    }

    public async Task JoinLobby(Guid groupId, Guid playerId)
    {
        var player = await _repository.GetPlayerAsync(playerId);
        if (player is null || player.GroupId != groupId)
            throw new HubException("Player is not a member of this group.");

        await Groups.AddToGroupAsync(Context.ConnectionId, groupId.ToString());
        _sessions.RegisterConnection(groupId, playerId, Context.ConnectionId);

        var lobby = await _mediator.Send(new GetEntertainmentLobbyQuery(groupId));
        await Clients.Group(groupId.ToString()).SendAsync("lobbyUpdated", lobby);
    }

    public async Task StartGame(Guid groupId, Guid playerId)
    {
        EnsureCaller(groupId, playerId);
        var result = await _mediator.Send(new StartEntertainmentGameCommand(groupId, playerId));
        _sessions.StartGame(groupId, result.TurnOrder);
        _sessions.StartRound(groupId, result.Round.RoundId, result.Round.RoundNumber, result.Round.TranslatorPlayerId);
        await BroadcastRound(groupId, result.Round);
    }

    public async Task RevealHint(Guid groupId, Guid playerId)
    {
        EnsureCaller(groupId, playerId);
        var round = _sessions.GetRound(groupId);
        if (round is null || round.TranslatorId != playerId)
            return;

        _sessions.RecordTranslatorHintReveal(groupId);
        var turnOrder = _sessions.GetTurnOrder(groupId);
        _sessions.ApplyDeltas(groupId, EntertainmentScoring.TranslatorRevealedHint(turnOrder, playerId));
        await BroadcastScores(groupId);
    }

    public async Task RevealAnswer(Guid groupId, Guid playerId)
    {
        EnsureCaller(groupId, playerId);
        var round = _sessions.GetRound(groupId);
        if (round is null || round.TranslatorId == playerId || round.AnswerRevealed)
            return;

        _sessions.RecordAnswerRevealed(groupId);
        var turnOrder = _sessions.GetTurnOrder(groupId);
        _sessions.ApplyDeltas(groupId, EntertainmentScoring.AnswerRevealed(turnOrder, playerId));
        await Clients.Group(groupId.ToString()).SendAsync("answerRevealed");
        await BroadcastScores(groupId);
    }

    public async Task Done(Guid groupId, Guid playerId)
    {
        EnsureCaller(groupId, playerId);
        var round = _sessions.GetRound(groupId);
        if (round is null || round.TranslatorId != playerId)
            return;

        if (_sessions.WasCleanSolve(groupId))
            _sessions.ApplyDeltas(groupId, EntertainmentScoring.CleanSolve(playerId));

        await FinalizeAndAdvance(groupId, round);
    }

    public async Task Skip(Guid groupId, Guid playerId)
    {
        EnsureCaller(groupId, playerId);
        var round = _sessions.GetRound(groupId);
        if (round is null || round.TranslatorId != playerId)
            return;

        var turnOrder = _sessions.GetTurnOrder(groupId);
        _sessions.ApplyDeltas(groupId, EntertainmentScoring.Skipped(turnOrder, playerId));

        await FinalizeAndAdvance(groupId, round);
    }

    public async Task EndGame(Guid groupId, Guid playerId)
    {
        EnsureCaller(groupId, playerId);
        var scores = _sessions.GetScores(groupId);
        var standings = await _mediator.Send(new EndEntertainmentGameCommand(groupId, playerId, scores));
        await Clients.Group(groupId.ToString()).SendAsync("gameEnded", standings);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _sessions.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task FinalizeAndAdvance(Guid groupId, GameSessionManager.RoundState round)
    {
        await BroadcastScores(groupId);

        var scores = _sessions.GetScores(groupId);
        await _mediator.Send(new FinalizeEntertainmentRoundCommand(groupId, round.RoundId, scores));

        var nextTranslator = _sessions.AdvanceTranslator(groupId);
        if (nextTranslator is null)
            return;

        var nextRound = await _mediator.Send(
            new StartNextEntertainmentRoundCommand(groupId, nextTranslator.Value, round.RoundNumber + 1));
        _sessions.StartRound(groupId, nextRound.RoundId, nextRound.RoundNumber, nextRound.TranslatorPlayerId);

        await BroadcastRound(groupId, nextRound);
    }

    private async Task BroadcastRound(Guid groupId, EntertainmentRoundDto round)
    {
        var translatorConnectionId = _sessions.GetConnectionId(groupId, round.TranslatorPlayerId);

        var translatorRound = round with { SentenceFi = null };
        if (translatorConnectionId is not null)
        {
            await Clients.Client(translatorConnectionId).SendAsync("roundStarted", translatorRound);
        }

        var exclude = translatorConnectionId is null
            ? Array.Empty<string>()
            : new[] { translatorConnectionId };
        await Clients.GroupExcept(groupId.ToString(), exclude)
            .SendAsync("roundStarted", round);
    }

    private void EnsureCaller(Guid groupId, Guid playerId)
    {
        if (_sessions.GetConnectionId(groupId, playerId) != Context.ConnectionId)
            throw new HubException("Not authorized to act as this player.");
    }

    private async Task BroadcastScores(Guid groupId)
    {
        var scores = _sessions.GetScores(groupId)
            .Select(kv => new { playerId = kv.Key, score = kv.Value })
            .ToList();
        await Clients.Group(groupId.ToString()).SendAsync("scoreUpdated", scores);
    }
}
