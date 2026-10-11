using CleaningSuite.Application.Entertainment;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace CleaningSuite.Api.Hubs;

/// <summary>
/// Realtime channel for the entertainment game. Clients join a SignalR group per
/// game group; the lobby list is loaded from the database and broadcast on every
/// join so everyone stays in sync. When the creator starts the game, the first
/// round is generated and pushed per-role (translator vs. observers).
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
        var result = await _mediator.Send(new StartEntertainmentGameCommand(groupId, playerId));
        _sessions.StartGame(groupId, result.TurnOrder);

        var translatorConnectionId = _sessions.GetConnectionId(groupId, result.Round.TranslatorPlayerId);

        // Translator sees English + base forms (no Finnish); observers also see Finnish.
        var translatorRound = result.Round with { SentenceFi = null };
        var observerRound = result.Round;

        if (translatorConnectionId is not null)
        {
            await Clients.Client(translatorConnectionId).SendAsync("roundStarted", translatorRound);
        }

        var exclude = translatorConnectionId is null
            ? Array.Empty<string>()
            : new[] { translatorConnectionId };
        await Clients.GroupExcept(groupId.ToString(), exclude)
            .SendAsync("roundStarted", observerRound);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _sessions.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
