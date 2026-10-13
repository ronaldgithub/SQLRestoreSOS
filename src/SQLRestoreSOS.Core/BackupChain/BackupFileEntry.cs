namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>One backup file discovered on disk (sp_DatabaseRestore) or in msdb history (sp_RestoreGene).</summary>
public sealed record BackupFileEntry(
    string FilePath,
    BackupType Type,
    DateTime BackupStartDate,
    DateTime BackupFinishDate,
    decimal FirstLsn,
    decimal LastLsn);
