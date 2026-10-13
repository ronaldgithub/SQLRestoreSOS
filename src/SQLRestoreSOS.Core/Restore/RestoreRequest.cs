namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// Engine-agnostic description of "what the DBA wants to do" - the Basic tab builds one of
/// these from its minimal fields, and each IRestoreEngine maps it onto its own full parameter set.
/// </summary>
public sealed class RestoreRequest
{
    public required string SourceDatabase { get; init; }

    public string? TargetDatabase { get; init; }

    public string TargetDatabaseOrDefault =>
        string.IsNullOrWhiteSpace(TargetDatabase) ? SourceDatabase : TargetDatabase!;

    public required string BackupPathFull { get; init; }

    public string? BackupPathDiff { get; init; }

    public string? BackupPathLog { get; init; }

    /// <summary>Null means "restore everything available" (latest point).</summary>
    public DateTime? PointInTimeUtc { get; init; }

    /// <summary>
    /// What to do if the target database already exists. Anything other than DoNothing is
    /// destructive (kills active connections and/or drops the database) and the UI must show
    /// an explicit warning/confirmation before a request carrying one of those values is run.
    /// </summary>
    public ExistingDatabaseAction ExistingDatabaseAction { get; init; } = ExistingDatabaseAction.DoNothing;

    public bool RunCheckDbAfterRestore { get; init; }

    public bool MoveFiles { get; init; }

    public string? MoveDataDrive { get; init; }

    public string? MoveLogDrive { get; init; }

    /// <summary>False = preview/dry-run only. True = actually run the restore.</summary>
    public bool ExecuteImmediately { get; init; }

    /// <summary>
    /// Log-shipping mode: always leave the database in NORECOVERY/STANDBY after applying backups,
    /// regardless of PointInTimeUtc, so further log backups can keep being applied later. The
    /// database is only brought online when the DBA explicitly finalizes via LogShippingSession.RecoverNowAsync.
    /// </summary>
    public bool KeepInNoRecoveryForLogShipping { get; init; }

    /// <summary>
    /// The target database is already in RESTORING state from an earlier NORECOVERY restore - skip
    /// the FULL/DIFF restore and only apply log backups newer than what it already has. Maps to
    /// sp_DatabaseRestore's @ContinueLogs = 1; only LogShippingSession's follow-up steps set this.
    /// It must stay false for a normal restore, or the proc skips the FULL backup entirely.
    /// </summary>
    public bool ContinueExistingRestore { get; init; }
}
