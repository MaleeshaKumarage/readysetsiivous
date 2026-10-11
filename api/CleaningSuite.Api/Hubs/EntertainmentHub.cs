using CleaningSuite.Application.Entertainment;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace CleaningSuite.Api.Hubs;

/// <summary>
/// Realtime channel for the entertainment game. Clients join a SignalR group per
/// game group; the lobby list is loaded from the database and broadcast on every
/// join so everyone stays in sync.
/// </summary>
public class EntertainmentHub : Hub
{
    private readonly IMediator _mediator;
    private readonly IEntertainmentRepository _repository;

    public EntertainmentHub(IMediator mediator, IEntertainmentRepository repository)
    {
        _mediator = mediator;
        _repository = repository;
    }

    public async Task JoinLobby(Guid groupId, Guid playerId)
    {
        var player = await _repository.GetPlayerAsync(playerId);
        if (player is null || player.GroupId != groupId)
            throw new HubException("Player is not a member of this group.");

        await Groups.AddToGroupAsync(Context.ConnectionId, groupId.ToString());

        var lobby = await _mediator.Send(new GetEntertainmentLobbyQuery(groupId));
        await Clients.Group(groupId.ToString()).SendAsync("lobbyUpdated", lobby);
    }
}
