using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Threading.Channels;
using CleaningSuite.Api;
using CleaningSuite.Api.Hubs;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace CleaningSuite.Tests.Entertainment;
public class EntertainmentHubIntegrationTests
{
    private sealed class Host:IAsyncDisposable
    {
        public readonly ConcurrentDictionary<Guid,EntertainmentGroup> Groups=new();
        public readonly ConcurrentDictionary<Guid,EntertainmentPlayer> Players=new();
        public readonly ConcurrentDictionary<Guid,EntertainmentRound> Rounds=new();
        public readonly Mock<IEntertainmentRepository> Repo=new();
        public readonly Mock<IGeminiSentenceService> Generator=new();
        public readonly Mock<IEntertainmentAnswerEvaluator> Evaluator=new();
        public readonly WebApplicationFactory<Program> Factory;
        public int GenerationCalls,EvaluationCalls;public bool FailGeneration,FailEvaluation;
        public Host(){
            Repo.Setup(r=>r.CreateGroupAsync(It.IsAny<EntertainmentGroup>(),It.IsAny<EntertainmentPlayer>(),It.IsAny<CancellationToken>())).Returns<EntertainmentGroup,EntertainmentPlayer,CancellationToken>((g,p,_)=>{Groups[g.Id]=g;Players[p.Id]=p;return Task.CompletedTask;});
            Repo.Setup(r=>r.GetGroupAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid id,CancellationToken _)=>Groups.GetValueOrDefault(id));
            Repo.Setup(r=>r.GetGroupByJoinKeyAsync(It.IsAny<string>(),It.IsAny<CancellationToken>())).ReturnsAsync((string key,CancellationToken _)=>Groups.Values.FirstOrDefault(g=>g.JoinKey==key));
            Repo.Setup(r=>r.GetPlayerAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid id,CancellationToken _)=>Players.GetValueOrDefault(id));
            Repo.Setup(r=>r.ListPlayersAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid id,CancellationToken _)=>(IReadOnlyList<EntertainmentPlayer>)Players.Values.Where(p=>p.GroupId==id).OrderBy(p=>p.JoinedUtc).ToList());
            Repo.Setup(r=>r.JoinPlayerAsync(It.IsAny<EntertainmentGroup>(),It.IsAny<EntertainmentPlayer>(),It.IsAny<CancellationToken>())).Returns<EntertainmentGroup,EntertainmentPlayer,CancellationToken>((g,p,_)=>{Players[p.Id]=p;Groups[g.Id]=g;return Task.CompletedTask;});
            Repo.Setup(r=>r.GetRoundAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid id,CancellationToken _)=>Rounds.GetValueOrDefault(id));
            Repo.Setup(r=>r.GetLatestRoundAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid id,CancellationToken _)=>Rounds.Values.Where(r=>r.GroupId==id).OrderByDescending(r=>r.RoundNumber).FirstOrDefault());
            Repo.Setup(r=>r.ListAllSentencesAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid id,CancellationToken _)=>(IReadOnlyList<string>)Rounds.Values.Where(r=>r.GroupId==id).Select(r=>r.SentenceEn).ToList());
            Repo.Setup(r=>r.SaveGeneratedRoundAsync(It.IsAny<EntertainmentGroup>(),It.IsAny<EntertainmentRound>(),It.IsAny<CancellationToken>())).Returns<EntertainmentGroup,EntertainmentRound,CancellationToken>((g,r,_)=>{Groups[g.Id]=g;Rounds[r.Id]=r;return Task.CompletedTask;});
            Repo.Setup(r=>r.SaveGameStateAsync(It.IsAny<EntertainmentGroup>(),It.IsAny<EntertainmentRound>(),It.IsAny<CancellationToken>())).Returns<EntertainmentGroup,EntertainmentRound,CancellationToken>((g,r,_)=>{Groups[g.Id]=g;Rounds[r.Id]=r;return Task.CompletedTask;});
            Repo.Setup(r=>r.SaveRoundAsync(It.IsAny<EntertainmentRound>(),It.IsAny<CancellationToken>())).Returns<EntertainmentRound,CancellationToken>((r,_)=>{Rounds[r.Id]=r;return Task.CompletedTask;});
            Repo.Setup(r=>r.SaveGroupAsync(It.IsAny<EntertainmentGroup>(),It.IsAny<CancellationToken>())).Returns<EntertainmentGroup,CancellationToken>((g,_)=>{Groups[g.Id]=g;return Task.CompletedTask;});
            Repo.Setup(r=>r.SavePlayerAsync(It.IsAny<EntertainmentPlayer>(),It.IsAny<CancellationToken>())).Returns<EntertainmentPlayer,CancellationToken>((p,_)=>{Players[p.Id]=p;return Task.CompletedTask;});
            Generator.Setup(r=>r.GenerateAsync(It.IsAny<string>(),It.IsAny<CefrLevel>(),It.IsAny<string>(),It.IsAny<IReadOnlyList<string>>(),It.IsAny<CancellationToken>())).ReturnsAsync(()=>{var n=Interlocked.Increment(ref GenerationCalls);if(FailGeneration)throw new EntertainmentAiUnavailableException("Question generation unavailable. Retry.");var text=n==1?"The cat sleeps peacefully.":n==2?"We repair the gym windows.":"She travels abroad tomorrow.";return new GeminiSentence(text,"Kissa nukkuu rauhallisesti.",["kissa","nukkua"]){Hints=[new("cat","kissa"),new("sleep","nukkua")]};});
            Evaluator.Setup(r=>r.EvaluateAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CefrLevel>(),It.IsAny<CancellationToken>())).ReturnsAsync(()=>{Interlocked.Increment(ref EvaluationCalls);if(FailEvaluation)throw new EntertainmentAiUnavailableException("Validation unavailable. Keep your answer and retry.");return new EntertainmentAnswerEvaluation(true,"Kissa nukkuu.","Correct Finnish subject and present-tense verb.",[],7,3);});
            Factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{foreach(var d in s.Where(d=>d.ImplementationType==typeof(MartenSchemaInitializer)||d.ImplementationType==typeof(QualityCycleWorker)||d.ImplementationType==typeof(CleaningSuite.Api.Services.EntertainmentGroupCleanupService)).ToList())s.Remove(d);s.RemoveAll<IEntertainmentRepository>();s.AddSingleton(Repo.Object);s.RemoveAll<IGeminiSentenceService>();s.AddSingleton(Generator.Object);s.RemoveAll<IEntertainmentAnswerEvaluator>();s.AddSingleton(Evaluator.Object);}));
        }
        public async Task<CreateEntertainmentGroupResult> Create(){using var client=Factory.CreateClient();var response=await client.PostAsJsonAsync("/api/v1/entertainment/groups",new{Name="Fixture",Description="A topic for owned tests",CreatorName="Host",CreatorLevel="A1"});response.EnsureSuccessStatusCode();return(await response.Content.ReadFromJsonAsync<CreateEntertainmentGroupResult>())!;}
        public async Task<JoinEntertainmentGroupResult> Join(string key){using var client=Factory.CreateClient();var response=await client.PostAsJsonAsync($"/api/v1/entertainment/groups/{key}/join",new{JoinKey=key,PlayerName="Observer",Level="A1"});response.EnsureSuccessStatusCode();return(await response.Content.ReadFromJsonAsync<JoinEntertainmentGroupResult>())!;}
        public async Task<Client> Connect(Guid group,Guid player,string token){var connection=new HubConnectionBuilder().WithUrl("http://localhost/hubs/entertainment",o=>{o.Transports=HttpTransportType.LongPolling;o.HttpMessageHandlerFactory=_=>Factory.Server.CreateHandler();}).Build();var client=new Client(connection);await connection.StartAsync();await connection.InvokeAsync("JoinLobby",group,player,token);return client;}
        public async ValueTask DisposeAsync()=>await Factory.DisposeAsync();
    }
    private sealed class Client:IAsyncDisposable
    {
        public HubConnection Connection{get;}public Channel<EntertainmentRoundDto> Round{get;}=Channel.CreateUnbounded<EntertainmentRoundDto>();public Channel<string> Errors{get;}=Channel.CreateUnbounded<string>();public Channel<object> Evaluations{get;}=Channel.CreateUnbounded<object>();public int AnswerEvents,HintEvents;
        public Client(HubConnection c){Connection=c;c.On<EntertainmentRoundDto>("roundStarted",r=>Round.Writer.TryWrite(r));c.On<string>("gameError",e=>Errors.Writer.TryWrite(e));c.On<object>("answerEvaluated",e=>Evaluations.Writer.TryWrite(e));c.On<object>("answerRevealed",_=>Interlocked.Increment(ref AnswerEvents));c.On<object>("hintRevealed",_=>Interlocked.Increment(ref HintEvents));}
        public async Task<EntertainmentRoundDto> Next()=>await Round.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8));
        public async ValueTask DisposeAsync()=>await Connection.DisposeAsync();
    }
    [Fact]
    public async Task ExposedPlayerIdCannotClaimHostAndRebindingRemovesOldRoomMembership()
    {
        await using var host=new Host();var game=await host.Create();var observer=await host.Join(game.JoinKey);
        await using var owner=await host.Connect(game.GroupId,game.CreatorPlayerId,game.PlayerToken);await using var other=await host.Connect(game.GroupId,observer.PlayerId,observer.PlayerToken);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(()=>other.Connection.InvokeAsync("JoinLobby",game.GroupId,game.CreatorPlayerId,observer.PlayerToken));
        await owner.Connection.InvokeAsync("StartGame",game.GroupId,game.CreatorPlayerId);var round=await owner.Next();Assert.Null(round.SentenceFi);Assert.All(round.Hints!,h=>Assert.Equal("",h.Finnish));
        await other.Connection.InvokeAsync("RevealHint",game.GroupId,observer.PlayerId,0,round.RoundId);Assert.Contains("Only the current translator",await other.Errors.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)));Assert.Equal(0,other.HintEvents);
        await using var replacement=await host.Connect(game.GroupId,game.CreatorPlayerId,game.PlayerToken);await replacement.Next();
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(()=>owner.Connection.InvokeAsync("CheckAnswer",game.GroupId,game.CreatorPlayerId,round.RoundId,"Kissa nukkuu."));
        await other.Connection.InvokeAsync("RevealAnswer",game.GroupId,observer.PlayerId,round.RoundId);
        await replacement.Connection.InvokeAsync("CheckAnswer",game.GroupId,game.CreatorPlayerId,round.RoundId,"Kissa nukkuu.");await replacement.Evaluations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(0,owner.AnswerEvents);Assert.Equal(0,replacement.AnswerEvents);Assert.Equal(1,host.EvaluationCalls);
    }
    [Fact]
    public async Task DuplicateHintsCheckAndNextDoNotRepeatScoresEvaluationOrGeneration()
    {
        await using var host=new Host();var game=await host.Create();var observer=await host.Join(game.JoinKey);
        await using var owner=await host.Connect(game.GroupId,game.CreatorPlayerId,game.PlayerToken);await using var other=await host.Connect(game.GroupId,observer.PlayerId,observer.PlayerToken);
        await owner.Connection.InvokeAsync("StartGame",game.GroupId,game.CreatorPlayerId);var round=await owner.Next();
        await owner.Connection.InvokeAsync("RevealHint",game.GroupId,game.CreatorPlayerId,0,round.RoundId);await owner.Connection.InvokeAsync("RevealHint",game.GroupId,game.CreatorPlayerId,0,round.RoundId);Assert.Equal(0,host.Groups[game.GroupId].Scores.GetValueOrDefault(observer.PlayerId));
        await owner.Connection.InvokeAsync("CheckAnswer",game.GroupId,game.CreatorPlayerId,round.RoundId,"Kissa nukkuu.");await owner.Connection.InvokeAsync("CheckAnswer",game.GroupId,game.CreatorPlayerId,round.RoundId,"Kissa nukkuu.");Assert.Equal(1,host.EvaluationCalls);Assert.Equal(10,host.Groups[game.GroupId].Scores[game.CreatorPlayerId]);Assert.Equal(10,host.Rounds[round.RoundId].Evaluation!.ScorePoints);Assert.Equal(0,host.Groups[game.GroupId].Scores.GetValueOrDefault(observer.PlayerId));
        await owner.Connection.InvokeAsync("NextRound",game.GroupId,game.CreatorPlayerId,round.RoundId);await owner.Connection.InvokeAsync("NextRound",game.GroupId,game.CreatorPlayerId,round.RoundId);Assert.Equal(2,host.GenerationCalls);Assert.Equal(2,host.Rounds.Count);
    }
    [Fact]
    public async Task ProviderFailurePreservesAnswerRoundAndTurnUntilRetryAndBlocksUncheckedNext()
    {
        await using var host=new Host();var game=await host.Create();await using var owner=await host.Connect(game.GroupId,game.CreatorPlayerId,game.PlayerToken);
        await owner.Connection.InvokeAsync("StartGame",game.GroupId,game.CreatorPlayerId);var round=await owner.Next();
        await owner.Connection.InvokeAsync("NextRound",game.GroupId,game.CreatorPlayerId,round.RoundId);Assert.Contains("AI must",await owner.Errors.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)));Assert.Equal(1,host.GenerationCalls);
        host.FailEvaluation=true;await owner.Connection.InvokeAsync("CheckAnswer",game.GroupId,game.CreatorPlayerId,round.RoundId,"Keep my Finnish answer");Assert.Contains("Keep your answer",await owner.Errors.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)));Assert.Equal("Keep my Finnish answer",host.Rounds[round.RoundId].SubmittedAnswer);Assert.Null(host.Rounds[round.RoundId].Evaluation);
        host.FailEvaluation=false;await owner.Connection.InvokeAsync("CheckAnswer",game.GroupId,game.CreatorPlayerId,round.RoundId,"Keep my Finnish answer");Assert.Equal(10,host.Groups[game.GroupId].Scores[game.CreatorPlayerId]);
        host.FailGeneration=true;await owner.Connection.InvokeAsync("NextRound",game.GroupId,game.CreatorPlayerId,round.RoundId);Assert.Contains("generation unavailable",await owner.Errors.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)));Assert.Equal(round.RoundId,host.Groups[game.GroupId].CurrentRoundId);Assert.Single(host.Rounds);
        host.FailGeneration=false;await owner.Connection.InvokeAsync("NextRound",game.GroupId,game.CreatorPlayerId,round.RoundId);Assert.Equal(2,host.Rounds.Count);Assert.Equal(10,host.Groups[game.GroupId].Scores[game.CreatorPlayerId]);
    }
}
