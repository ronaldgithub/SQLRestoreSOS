using Microsoft.Data.SqlClient;
using SQLRestoreSOS.Core.BackupChain;
using SQLRestoreSOS.Core.BackupChain.Engines;

namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// Orchestrates a "log shipping" style restore: the initial Full(+Diff)+Logs restore is applied
/// WITH NORECOVERY, and the session can then be polled for newly-arrived log backups so the UI
/// can prompt "a new log backup was found - apply it?" and keep the database in NORECOVERY after
/// each one, until the DBA explicitly finalizes (brings it online WITH RECOVERY).
/// </summary>
public sealed class LogShippingSession
{
    private readonly DatabaseRestoreEngine _engine;
    private readonly RestoreRequest _template;

    public DateTime LastAppliedFinishDateUtc { get; private set; }

    public bool IsFinalized { get; private set; }

    public LogShippingSession(DatabaseRestoreEngine engine, RestoreRequest initialRequest, DateTime lastAppliedFinishDateUtc)
    {
        if (string.IsNullOrWhiteSpace(initialRequest.BackupPathLog))
        {
            throw new ArgumentException("A log shipping session requires BackupPathLog to be set.", nameof(initialRequest));
        }

        _engine = engine;
        _template = initialRequest;
        LastAppliedFinishDateUtc = lastAppliedFinishDateUtc;
    }

    /// <summary>Builds the initial request this session should run with (always stays in NORECOVERY).</summary>
    public static RestoreRequest BuildInitialRequest(RestoreRequest request) => Clone(request, request.PointInTimeUtc, executeImmediately: request.ExecuteImmediately, continueExistingRestore: false);

    /// <summary>Checks the log folder for backups newer than the last one applied in this session.</summary>
    public Task<IReadOnlyList<BackupFileEntry>> CheckForNewLogsAsync(SqlConnection conn, CancellationToken ct = default) =>
        _engine.FindNewLogBackupsAsync(conn, _template.BackupPathLog!, LastAppliedFinishDateUtc, ct);

    /// <summary>
    /// Applies one newly-found log backup, keeping the database in NORECOVERY so more logs can
    /// be applied later. Returns the request that was executed (useful for the SQL preview pane).
    /// </summary>
    public async Task<RestoreRequest> ApplyLogAsync(
        SqlConnection conn,
        BackupFileEntry newLog,
        IProgress<RestoreProgressEvent>? progress,
        CancellationToken ct = default)
    {
        if (IsFinalized)
        {
            throw new InvalidOperationException("This log shipping session has already been finalized (recovered).");
        }

        var request = Clone(_template, newLog.BackupFinishDate, executeImmediately: true, continueExistingRestore: true);
        var parameters = _engine.GetDefaultParameters(request);

        await _engine.ExecuteAsync(conn, request, parameters, progress, ct).ConfigureAwait(false);

        LastAppliedFinishDateUtc = newLog.BackupFinishDate;
        return request;
    }

    /// <summary>Brings the database online WITH RECOVERY, ending the log shipping session.</summary>
    public async Task RecoverNowAsync(SqlConnection conn, IProgress<RestoreProgressEvent>? progress, CancellationToken ct = default)
    {
        var finalRequest = CloneWithRecovery(_template);
        var parameters = _engine.GetDefaultParameters(finalRequest);

        await _engine.ExecuteAsync(conn, finalRequest, parameters, progress, ct).ConfigureAwait(false);
        IsFinalized = true;
    }

    private static RestoreRequest Clone(RestoreRequest source, DateTime? pointInTimeUtc, bool executeImmediately, bool continueExistingRestore) => new()
    {
        SourceDatabase = source.SourceDatabase,
        TargetDatabase = source.TargetDatabase,
        BackupPathFull = source.BackupPathFull,
        BackupPathDiff = source.BackupPathDiff,
        BackupPathLog = source.BackupPathLog,
        PointInTimeUtc = pointInTimeUtc,
        ExistingDatabaseAction = ExistingDatabaseAction.DoNothing, // the DB is already in NORECOVERY from the previous step
        RunCheckDbAfterRestore = false,
        MoveFiles = source.MoveFiles,
        MoveDataDrive = source.MoveDataDrive,
        MoveLogDrive = source.MoveLogDrive,
        ExecuteImmediately = executeImmediately,
        KeepInNoRecoveryForLogShipping = true,
        ContinueExistingRestore = continueExistingRestore
    };

    private static RestoreRequest CloneWithRecovery(RestoreRequest source) => new()
    {
        SourceDatabase = source.SourceDatabase,
        TargetDatabase = source.TargetDatabase,
        BackupPathFull = source.BackupPathFull,
        BackupPathDiff = source.BackupPathDiff,
        BackupPathLog = source.BackupPathLog,
        PointInTimeUtc = source.PointInTimeUtc,
        ExistingDatabaseAction = ExistingDatabaseAction.DoNothing,
        RunCheckDbAfterRestore = source.RunCheckDbAfterRestore,
        MoveFiles = source.MoveFiles,
        MoveDataDrive = source.MoveDataDrive,
        MoveLogDrive = source.MoveLogDrive,
        ExecuteImmediately = true,
        KeepInNoRecoveryForLogShipping = false, // this is the step that finally runs WITH RECOVERY
        ContinueExistingRestore = true // the database is already restoring - don't restart from the FULL
    };
}
