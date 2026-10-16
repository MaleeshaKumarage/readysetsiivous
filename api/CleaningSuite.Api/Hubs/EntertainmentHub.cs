using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using MediatR;
using Microsoft.AspNetCore.SignalR;
namespace CleaningSuite.Api.Hubs;

public class EntertainmentHub(IMediator mediator,IEntertainmentRepository repository,GameSessionManager sessions,IEntertainmentAnswerEvaluator evaluator,ILogger<EntertainmentHub> logger):Hub
{
    private CancellationToken Ct=>Context.ConnectionAborted;
    public async Task JoinLobby(Guid groupId,Guid playerId,string playerToken)
    {
        var player=await repository.GetPlayerAsync(playerId,Ct);
        if(player is null||player.GroupId!=groupId||!EntertainmentPlayerToken.Verify(playerToken,player.PlayerTokenHash))throw new HubException("Your player session is invalid. Rejoin with the group key.");
        await using var lease=await sessions.AcquireAsync(groupId,Ct);
        var membership=sessions.GetMembership(Context.ConnectionId);
        if(membership.HasValue && membership.Value.Group!=groupId)throw new HubException("Leave your current group before joining another.");
        var old=sessions.GetConnectionId(groupId,playerId);
        if(old is not null && old!=Context.ConnectionId)await Groups.RemoveFromGroupAsync(old,groupId.ToString(),Ct);
        sessions.RegisterConnection(groupId,playerId,Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId,groupId.ToString(),Ct);
        await BroadcastLobby(groupId);
        await Snapshot(groupId,playerId,Context.ConnectionId);
    }
    public Task StartGame(Guid groupId,Guid playerId)=>Action(groupId,playerId,async()=>{
        var group=await GetGroup(groupId);
        if(group.Status!=EntertainmentStatus.Lobby){await Snapshot(groupId,playerId,Context.ConnectionId);return;}
        await mediator.Send(new StartEntertainmentGameCommand(groupId,playerId),Ct);
        await BroadcastSnapshot(groupId);
    });
    public Task RevealHint(Guid groupId,Guid playerId,int index,Guid roundId)=>Action(groupId,playerId,async()=>{
        var (group,round)=await Current(groupId,roundId);
        RequireTranslator(round,playerId);
        if(index<0||index>=round.Hints.Count)throw new HubException("Invalid hint.");
        if(round.TranslatorPlayerId==playerId && !round.RevealedHintIndexes.Contains(index)){
            if(round.Evaluation is not null)throw new HubException("This answer has already been checked.");
            round.RevealedHintIndexes.Add(index);
            await repository.SaveGameStateAsync(group,round,Ct);
        }
        await Clients.Caller.SendAsync("hintRevealed",new {roundId,index,finnish=round.Hints[index].Finnish},Ct);
    });
    public Task RevealAnswer(Guid groupId,Guid playerId,Guid roundId)=>Action(groupId,playerId,async()=>{
        var (group,round)=await Current(groupId,roundId);
        if(round.TranslatorPlayerId==playerId)throw new HubException("Type your answer before seeing its correction.");
        if(!round.AnswerRevealed){round.AnswerRevealed=true;await repository.SaveGameStateAsync(group,round,Ct);}
        foreach(var id in group.TurnOrder.Where(id=>id!=round.TranslatorPlayerId))if(sessions.GetConnectionId(groupId,id) is { } connection)await Clients.Client(connection).SendAsync("answerRevealed",new{roundId,sentenceFi=round.SentenceFi},Ct);
    });
    public Task CheckAnswer(Guid groupId,Guid playerId,Guid roundId,string answer)=>Action(groupId,playerId,async()=>{
        var (group,round)=await Current(groupId,roundId);RequireTranslator(round,playerId);
        if(round.Evaluation is not null){if(!string.Equals(round.SubmittedAnswer,answer?.Trim(),StringComparison.Ordinal))throw new HubException("This answer has already been checked and cannot be changed.");await BroadcastEvaluation(group,round);return;}
        answer=answer?.Trim()??"";
        if(answer.Length is <1 or >2000)throw new HubException("Enter a Finnish answer of 1–2000 characters.");
        var player=await repository.GetPlayerAsync(playerId,Ct)??throw new HubException("Player not found.");
        round.SubmittedAnswer=answer;
        await repository.SaveGameStateAsync(group,round,Ct);
        var evaluation=await evaluator.EvaluateAsync(round.SentenceEn,round.SentenceFi,answer,player.Level,Ct);
        Ct.ThrowIfCancellationRequested();
        var roundPoints=EntertainmentScoring.ScoreAnswer(evaluation);
        evaluation=evaluation with{ScorePoints=roundPoints};
        round.SubmittedAnswer=answer;round.Evaluation=evaluation;
        group.Scores[playerId]=group.Scores.GetValueOrDefault(playerId)+roundPoints;
        group.UpdatedUtc=DateTime.UtcNow;
        await repository.SaveGameStateAsync(group,round,Ct);await BroadcastScores(group);await BroadcastEvaluation(group,round);
    });
    public Task NextRound(Guid groupId,Guid playerId,Guid roundId)=>Action(groupId,playerId,async()=>{
        var group=await GetGroup(groupId);
        var round=await repository.GetRoundAsync(roundId,Ct)??throw new HubException("Round not found.");
        if(round.GroupId!=groupId)throw new HubException("Invalid round.");RequireTranslator(round,playerId);
        if(group.Status!=EntertainmentStatus.InGame)throw new HubException("The game is not running.");
        if(group.CurrentRoundId!=roundId){await Snapshot(groupId,playerId,Context.ConnectionId);return;}
        if(round.Evaluation is null)throw new HubException("AI must check your answer before the next question.");
        var index=group.TurnOrder.IndexOf(playerId);Guid? next=null;
        for(var step=1;step<=group.TurnOrder.Count;step++){var id=group.TurnOrder[(index+step)%group.TurnOrder.Count];if(sessions.IsConnected(groupId,id)){next=id;break;}}
        if(next is null)throw new HubException("Waiting for a player to reconnect. Please retry.");
        var result=await mediator.Send(new StartNextEntertainmentRoundCommand(groupId,next.Value,round.RoundNumber+1),Ct);
        // New round + group are already durable. Linking the previous round is advisory;
        // CurrentRoundId prevents a repeated Next action from generating again.
        round.NextRoundId=result.RoundId;round.EndedUtc=DateTime.UtcNow;round.FinalScores=new(group.Scores);
        await repository.SaveRoundAsync(round,Ct);await BroadcastSnapshot(groupId);
    });
    public Task EndGame(Guid groupId,Guid playerId)=>Action(groupId,playerId,async()=>{
        var group=await GetGroup(groupId);
        var standings=await mediator.Send(new EndEntertainmentGameCommand(groupId,playerId,group.Scores),Ct);
        await Clients.Group(groupId.ToString()).SendAsync("gameEnded",standings,Ct);
    });
    public async Task LeaveLobby(Guid groupId,Guid playerId){EnsureCaller(groupId,playerId);await using var lease=await sessions.AcquireAsync(groupId,Ct);await Groups.RemoveFromGroupAsync(Context.ConnectionId,groupId.ToString(),Ct);sessions.RemoveConnection(Context.ConnectionId);await BroadcastLobby(groupId);}
    public override async Task OnDisconnectedAsync(Exception? exception){var membership=sessions.GetMembership(Context.ConnectionId);sessions.RemoveConnection(Context.ConnectionId);if(membership.HasValue)await BroadcastLobby(membership.Value.Group);await base.OnDisconnectedAsync(exception);}
    private async Task Action(Guid groupId,Guid playerId,Func<Task> action){EnsureCaller(groupId,playerId);sessions.CheckRate(Context.ConnectionId);try{await using var lease=await sessions.AcquireAsync(groupId,Ct);EnsureCaller(groupId,playerId);await action();}catch(OperationCanceledException)when(Ct.IsCancellationRequested){throw;}catch(Exception ex){var message=ex is EntertainmentAiUnavailableException or HubException?ex.Message:"The game action could not be completed. Please retry.";logger.LogWarning("Game action failed with {Category}.",ex.GetType().Name);await Clients.Caller.SendAsync("gameError",message,Ct);}}
    private void EnsureCaller(Guid groupId,Guid playerId){if(sessions.GetConnectionId(groupId,playerId)!=Context.ConnectionId)throw new HubException("Not authorized to act as this player.");}
    private async Task<EntertainmentGroup> GetGroup(Guid id)=>await repository.GetGroupAsync(id,Ct)??throw new HubException("Group not found.");
    private async Task<(EntertainmentGroup,EntertainmentRound)> Current(Guid groupId,Guid roundId){var group=await GetGroup(groupId);if(group.Status!=EntertainmentStatus.InGame||group.CurrentRoundId!=roundId)throw new HubException("This round is no longer current.");var round=await repository.GetRoundAsync(roundId,Ct)??throw new HubException("Round not found.");if(round.GroupId!=groupId)throw new HubException("Invalid round.");return(group,round);}
    private static void RequireTranslator(EntertainmentRound round,Guid playerId){if(round.TranslatorPlayerId!=playerId)throw new HubException("Only the current translator can submit or advance this round.");}
    private async Task BroadcastLobby(Guid id){var lobby=await mediator.Send(new GetEntertainmentLobbyQuery(id));await Clients.Group(id.ToString()).SendAsync("lobbyUpdated",lobby with{Players=lobby.Players.Select(p=>p with{IsOnline=sessions.IsConnected(id,p.PlayerId)}).ToList()});}
    private Task BroadcastScores(EntertainmentGroup group)=>Clients.Group(group.Id.ToString()).SendAsync("scoreUpdated",group.Scores.Select(s=>new{playerId=s.Key,score=s.Value}).ToList(),Ct);
    private Task BroadcastEvaluation(EntertainmentGroup group,EntertainmentRound round)=>Clients.Group(group.Id.ToString()).SendAsync("answerEvaluated",new{roundId=round.Id,answer=round.SubmittedAnswer,evaluation=round.Evaluation},Ct);
    private async Task BroadcastSnapshot(Guid groupId){var group=await GetGroup(groupId);foreach(var player in await repository.ListPlayersAsync(groupId,Ct))if(sessions.GetConnectionId(groupId,player.Id)is{} connection)await Snapshot(groupId,player.Id,connection);await BroadcastLobby(groupId);}
    private async Task Snapshot(Guid groupId,Guid playerId,string connection){var group=await GetGroup(groupId);await Clients.Client(connection).SendAsync("scoreUpdated",group.Scores.Select(s=>new{playerId=s.Key,score=s.Value}).ToList(),Ct);if(group.Status==EntertainmentStatus.Finished){var players=await repository.ListPlayersAsync(groupId,Ct);await Clients.Client(connection).SendAsync("gameEnded",players.OrderByDescending(p=>group.Scores.GetValueOrDefault(p.Id)).ThenBy(p=>p.JoinedUtc).Select(p=>new EntertainmentStandingDto(p.Id,p.Name,group.Scores.GetValueOrDefault(p.Id),p.IsCreator)).ToList(),Ct);return;}if(group.Status!=EntertainmentStatus.InGame)return;var round=group.CurrentRoundId.HasValue?await repository.GetRoundAsync(group.CurrentRoundId.Value,Ct):await repository.GetLatestRoundAsync(groupId,Ct);if(round is null)throw new HubException("Game state is unavailable. Please retry.");var dto=new EntertainmentRoundDto(round.Id,round.RoundNumber,round.TranslatorPlayerId,round.SentenceEn,round.TranslatorPlayerId!=playerId&&round.AnswerRevealed?round.SentenceFi:null,Array.Empty<string>(),round.Hints.Select(h=>new EntertainmentWordHint(h.English,"")).ToList(),round.SubmittedAnswer,round.Evaluation);await Clients.Client(connection).SendAsync("roundStarted",dto,Ct);if(round.TranslatorPlayerId==playerId)foreach(var index in round.RevealedHintIndexes)if(index>=0&&index<round.Hints.Count)await Clients.Client(connection).SendAsync("hintRevealed",new{roundId=round.Id,index,finnish=round.Hints[index].Finnish},Ct);if(round.Evaluation is not null)await Clients.Client(connection).SendAsync("answerEvaluated",new{roundId=round.Id,answer=round.SubmittedAnswer,evaluation=round.Evaluation},Ct);}
}
