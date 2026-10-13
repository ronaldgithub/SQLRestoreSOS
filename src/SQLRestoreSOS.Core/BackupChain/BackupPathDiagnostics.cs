namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>
/// Catches common "works in Explorer, fails for SQL Server" backup-path mistakes before they
/// turn into a confusing proc error.
/// </summary>
public static class BackupPathDiagnostics
{
    /// <summary>
    /// True for a drive-letter path (e.g. "W:\Backups\") as opposed to a UNC path
    /// (e.g. "\\server\share\Backups\"). Mapped drive letters are per-interactive-session in
    /// Windows, so the SQL Server service account (which isn't an interactive session) can't see
    /// them even though they work fine in Explorer for the DBA who mapped them - sp_DatabaseRestore
    /// then reports an empty/missing folder (via xp_dirtree or "DIR") that looks like a permissions
    /// or typo problem but is actually this. The fix is always the same: use the full UNC path.
    /// </summary>
    public static bool IsMappedDriveLetterPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.Length >= 2 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        !path.StartsWith(@"\\", StringComparison.Ordinal);
}
