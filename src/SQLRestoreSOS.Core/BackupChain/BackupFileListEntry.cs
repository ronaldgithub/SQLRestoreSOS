namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>One row of a RESTORE FILELISTONLY result - the files contained inside a single backup.</summary>
public sealed record BackupFileListEntry(
    string LogicalName,
    string PhysicalName,
    char Type, // 'D' = data, 'L' = log, 'F' = full-text/filestream
    string? FileGroupName,
    long Size,
    long MaxSize);
