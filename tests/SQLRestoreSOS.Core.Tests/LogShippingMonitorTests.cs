using SQLRestoreSOS.Core.BackupChain;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.Core.Tests;

public class LogShippingMonitorTests
{
    private static BackupFileEntry Log(string name) =>
        new(name, BackupType.Log, DateTime.Today, DateTime.Today, 1, 2);

    [Fact]
    public async Task PollOnce_WithNoNewLogs_DoesNotCallHandler()
    {
        var handlerCalled = false;
        var monitor = new LogShippingMonitor(
            _ => Task.FromResult<IReadOnlyList<BackupFileEntry>>([]),
            _ => { handlerCalled = true; return Task.FromResult(true); });

        var keepGoing = await monitor.PollOnceAsync();

        Assert.True(keepGoing);
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task PollOnce_PassesNewLogsToHandler_AndReturnsItsDecision()
    {
        IReadOnlyList<BackupFileEntry>? received = null;
        var monitor = new LogShippingMonitor(
            _ => Task.FromResult<IReadOnlyList<BackupFileEntry>>([Log("a.trn"), Log("b.trn")]),
            logs => { received = logs; return Task.FromResult(false); });

        var keepGoing = await monitor.PollOnceAsync();

        Assert.False(keepGoing);
        Assert.Equal(2, received!.Count);
    }

    [Fact]
    public async Task Start_PollsOnTimer_AndStopsWhenHandlerDeclines()
    {
        var polls = 0;
        var handled = new TaskCompletionSource();
        var monitor = new LogShippingMonitor(
            _ => { Interlocked.Increment(ref polls); return Task.FromResult<IReadOnlyList<BackupFileEntry>>([Log("a.trn")]); },
            _ => { handled.TrySetResult(); return Task.FromResult(false); });

        monitor.Start(TimeSpan.FromSeconds(1));
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);

        Assert.False(monitor.IsRunning);
        Assert.Equal(1, polls);
    }

    [Fact]
    public async Task Start_WhenCheckThrows_ReportsErrorAndStops()
    {
        var error = new TaskCompletionSource<Exception>();
        var monitor = new LogShippingMonitor(
            _ => throw new InvalidOperationException("boom"),
            _ => Task.FromResult(true),
            onError: ex => error.TrySetResult(ex));

        monitor.Start(TimeSpan.FromSeconds(1));
        var ex = await error.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("boom", ex.Message);
        Assert.False(monitor.IsRunning);
    }
}
