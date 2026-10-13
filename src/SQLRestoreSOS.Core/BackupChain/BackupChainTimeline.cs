namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>
/// The ordered Full -> Diff -> Log chain for one database, used to drive the
/// Basic tab's visual "pick a point in time" timeline control.
/// </summary>
public sealed class BackupChainTimeline
{
    public IReadOnlyList<BackupFileEntry> Entries { get; }

    public BackupChainTimeline(IEnumerable<BackupFileEntry> entries)
    {
        Entries = entries.OrderBy(e => e.BackupFinishDate).ToList();
    }

    public BackupFileEntry? LatestFull =>
        Entries.Where(e => e.Type == BackupType.Full)
               .OrderByDescending(e => e.BackupFinishDate)
               .FirstOrDefault();

    public IReadOnlyList<BackupFileEntry> DiffsAfter(DateTime fullBackupFinishDate) =>
        Entries.Where(e => e.Type == BackupType.Differential && e.BackupFinishDate > fullBackupFinishDate)
               .OrderBy(e => e.BackupFinishDate)
               .ToList();

    public IReadOnlyList<BackupFileEntry> LogsAfter(DateTime point) =>
        Entries.Where(e => e.Type == BackupType.Log && e.BackupFinishDate > point)
               .OrderBy(e => e.BackupFinishDate)
               .ToList();

    /// <summary>The earliest point in time a restore could stop at (the latest full backup's finish time).</summary>
    public DateTime? EarliestPossiblePointInTime => LatestFull?.BackupFinishDate;

    /// <summary>The latest point in time a restore could stop at (the most recent backup of any type).</summary>
    public DateTime? LatestPossiblePointInTime => Entries.Count == 0 ? null : Entries.Max(e => e.BackupFinishDate);
}
