using System.Threading.Channels;
using CleaningSuite.Application.Entertainment;

namespace CleaningSuite.Infrastructure.Entertainment;

public sealed class EntertainmentAiPromptQueue : BackgroundService
{
    private sealed record Work(Func<CancellationToken, Task<object>> Run, CancellationToken Token, TaskCompletionSource<object> Completion);
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });

    public async Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> run, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(180));
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new Work(async ct => (object)(await run(ct))!, deadline.Token, completion);
        try { await _queue.Writer.WriteAsync(work, deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new EntertainmentAiUnavailableException(); }
        using var registration = deadline.Token.Register(() => completion.TrySetCanceled(deadline.Token));
        try { return (T)await completion.Task; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new EntertainmentAiUnavailableException(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (work.Token.IsCancellationRequested) { work.Completion.TrySetCanceled(work.Token); continue; }
            try { work.Completion.TrySetResult(await work.Run(work.Token)); }
            catch (OperationCanceledException) when (work.Token.IsCancellationRequested) { work.Completion.TrySetCanceled(work.Token); }
            catch (Exception error) { work.Completion.TrySetException(error); }
        }
    }

    public override void Dispose() { _queue.Writer.TryComplete(); base.Dispose(); }
}
