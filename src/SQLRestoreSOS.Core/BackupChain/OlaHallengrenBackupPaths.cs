namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>
/// Ola Hallengren's Maintenance Solution (the de-facto standard SQL Server backup job) lays
/// backups out as &lt;BackupRoot&gt;\&lt;InstanceName&gt;\&lt;DatabaseName&gt;\{FULL|DIFF|LOG}\ -
/// e.g. \\192.168.178.15\SQLBackup\WIN10\StackOverflow2010\FULL\. This is treated as the app's
/// default folder convention: given just a backup root share, the Full/Diff/Log folders for any
/// database can be derived without the DBA browsing to three folders by hand.
/// </summary>
public static class OlaHallengrenBackupPaths
{
    public static (string Full, string Diff, string Log) Build(string backupRoot, string serverName, string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var trimmedRoot = backupRoot.TrimEnd('\\', '/');

        // Named instances (SERVER\INSTANCE) can't appear as a path segment with the backslash
        // intact - Ola Hallengren's own jobs sanitize this the same way (SERVER$INSTANCE).
        var instanceFolder = serverName.Replace('\\', '$');

        // The DBA may have already browsed straight to the per-database folder (it's the obvious
        // thing to pick in Explorer - it's the folder that visibly contains FULL/DIFF/LOG) rather
        // than the share root. Appending the instance/database segments again would then duplicate
        // the path, so detect that and use the given root as-is instead. The instance-folder segment
        // is checked loosely (database name alone is enough to match) because the connected server
        // name and the on-disk folder name commonly differ - e.g. connecting via "localhost" or an
        // IP while the backup folder uses the machine's real NetBIOS name.
        var baseDir = EndsWithSegment(trimmedRoot, databaseName)
            ? trimmedRoot
            : Path.Combine(trimmedRoot, instanceFolder, databaseName);

        return (
            Path.Combine(baseDir, "FULL") + "\\",
            Path.Combine(baseDir, "DIFF") + "\\",
            Path.Combine(baseDir, "LOG") + "\\");
    }

    private static bool EndsWithSegment(string path, string segment)
    {
        var lastSegment = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
            ? parts[^1]
            : string.Empty;

        return string.Equals(lastSegment, segment, StringComparison.OrdinalIgnoreCase);
    }
}
