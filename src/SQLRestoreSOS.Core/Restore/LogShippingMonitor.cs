using SQLRestoreSOS.Core.BackupChain;

namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// Polls for newly-arrived log backups on a timer during a log-shipping session. It only detects and hands
/// the new files to <c>onNewLogs</c> (the UI prompts the DBA - nothing is applied automatically). The check
/// and the handler are delegates so the loop is testable without a SQL Server.
/// </summary>
public sealed class LogShippingMonitor : IDisposable
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<BackupFileEntry>>> _check;
    private readonly Func<IReadOnlyList<BackupFileEntry>, Task<bool>> _onNewLogs;
    private readonly Action<Exception>? _onError;
    private readonly Action<DateTime, int>? _onChecked;
    private CancellationTokenSource? _cts;

    /// <param name="check">Returns log backups not yet applied (e.g. LogShippingSession.CheckForNewLogsAsync).</param>
    /// <param name="onNewLogs">Called with the new logs; return false to stop polling (e.g. the DBA skipped one).</param>
    /// <param name="onError">Called when a poll throws; polling stops afterwards.</param>
    /// <param name="onChecked">Called after every successful poll with the check time and number of new logs.</param>
    public LogShippingMonitor(
        Func<CancellationToken, Task<IReadOnlyList<BackupFileEntry>>> check,
        Func<IReadOnlyList<BackupFileEntry>, Task<bool>> onNewLogs,
        Action<Exception>? onError = null,
        Action<DateTime, int>? onChecked = null)
    {
        _check = check;
        _onNewLogs = onNewLogs;
        _onError = onError;
        _onChecked = onChecked;
    }

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    /// <summary>Runs one check now. Returns false when polling should stop.</summary>
    public async Task<bool> PollOnceAsync(CancellationToken ct = default)
    {
        var newLogs = await _check(ct).ConfigureAwait(false);
        _onChecked?.Invoke(DateTime.Now, newLogs.Count);

        return newLogs.Count == 0 || await _onNewLogs(newLogs).ConfigureAwait(false);
    }

    public void Start(TimeSpan interval)
    {
        if (IsRunning)
        {
            return;
        }

        if (interval < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Polling interval must be at least one second.");
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => LoopAsync(interval, token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task LoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (!await PollOnceAsync(ct).ConfigureAwait(false))
                {
                    Stop();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the caller.
        }
        catch (Exception ex)
        {
            Stop();
            _onError?.Invoke(ex);
        }
    }

    public void Dispose() => Stop();
}
