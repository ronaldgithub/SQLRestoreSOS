namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// Mirrors sp_DatabaseRestore's @ExistingDBAction tinyint. What to do if the target database
/// already exists. Anything other than DoNothing is destructive and must be gated behind an
/// explicit "this will kill active connections / overwrite data" confirmation in the UI.
/// </summary>
public enum ExistingDatabaseAction
{
    /// <summary>Safe default - the restore fails if the target database already exists.</summary>
    DoNothing = 0,

    /// <summary>Forcibly disconnects all active sessions on the target database, then overwrites it.</summary>
    KillConnections = 1,

    /// <summary>Takes the existing database offline before overwriting it.</summary>
    TakeOffline = 2,

    /// <summary>Drops the existing database outright before restoring.</summary>
    Drop = 3
}
