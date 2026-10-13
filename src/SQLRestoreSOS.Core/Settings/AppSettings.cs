namespace SQLRestoreSOS.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string Theme { get; set; } = "Dark";

    public RestoreEngineKind RestoreEngine { get; set; } = RestoreEngineKind.DatabaseRestore;

    public List<RecentServerEntry> RecentServers { get; set; } = new();

    public Dictionary<string, BackupPathSet> DefaultBackupPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Remembered Ola Hallengren backup share root per server (e.g. \\192.168.178.15\SQLBackup) -
    /// the Full/Diff/Log folders for any database are derived from this via
    /// <see cref="BackupChain.OlaHallengrenBackupPaths"/> rather than browsed to individually.
    /// </summary>
    public Dictionary<string, string> BackupRootPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
