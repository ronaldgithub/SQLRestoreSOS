namespace SQLRestoreSOS.Core.BackupChain;

public sealed record BackupChainQuery(
    string DatabaseName,
    string? BackupPathFull,
    string? BackupPathDiff,
    string? BackupPathLog);
