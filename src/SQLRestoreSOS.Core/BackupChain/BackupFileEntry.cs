namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>
/// One backup file discovered on disk (sp_DatabaseRestore) or in msdb history (sp_RestoreGene).
/// <paramref name="DatabaseBackupLsn"/> / <paramref name="CheckpointLsn"/> come from RESTORE HEADERONLY and
/// let <see cref="BackupChainAnalyzer"/> link a differential to its base full; they are null when the
/// source didn't provide them (the analyzer then falls back to backup dates).
/// </summary>
public sealed record BackupFileEntry(
    string FilePath,
    BackupType Type,
    DateTime BackupStartDate,
    DateTime BackupFinishDate,
    decimal FirstLsn,
    decimal LastLsn,
    decimal? DatabaseBackupLsn = null,
    decimal? CheckpointLsn = null);
