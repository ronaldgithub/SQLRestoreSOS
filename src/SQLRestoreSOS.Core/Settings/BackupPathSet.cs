namespace SQLRestoreSOS.Core.Settings;

/// <summary>Remembered Full/Diff/Log backup folders for one server, so the DBA doesn't re-type them.</summary>
public sealed class BackupPathSet
{
    public string? Full { get; set; }

    public string? Diff { get; set; }

    public string? Log { get; set; }
}
