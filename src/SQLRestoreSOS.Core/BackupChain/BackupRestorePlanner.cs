namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>
/// The backups that would be applied, in order, for a chosen point in time. <paramref name="Problem"/> is set
/// (and <paramref name="Steps"/> empty) when the point in time cannot be restored from the discovered backups.
/// </summary>
public sealed record BackupRestorePlan(IReadOnlyList<BackupFileEntry> Steps, string? Problem);

/// <summary>
/// Pure, offline: which Full / Differential / Log files a restore to <c>target</c> needs. Used by the timeline
/// dialog to mark files as the point-in-time slider moves. Mirrors what sp_DatabaseRestore does with @StopAt
/// (latest full and diff that finished by then, then log backups up to and including the one covering it).
/// </summary>
public static class BackupRestorePlanner
{
    /// <param name="target">The point in time, or null for "restore to latest" (no STOPAT).</param>
    public static BackupRestorePlan Plan(IReadOnlyList<BackupChainRow> rows, DateTime? target)
    {
        var restorable = rows.Where(r => r.IsRestorable).Select(r => r.Entry).ToList();

        var baseFull = restorable
            .Where(e => e.Type == BackupType.Full && (target is null || e.BackupFinishDate <= target))
            .OrderByDescending(e => e.BackupFinishDate)
            .FirstOrDefault();

        if (baseFull is null)
        {
            return new BackupRestorePlan([], "No full backup had finished by this point in time.");
        }

        var steps = new List<BackupFileEntry> { baseFull };

        var diff = restorable
            .Where(e => e.Type == BackupType.Differential
                        && (target is null || e.BackupFinishDate <= target)
                        && IsBasedOn(e, baseFull))
            .OrderByDescending(e => e.BackupFinishDate)
            .FirstOrDefault();

        if (diff is not null)
        {
            steps.Add(diff);
        }

        // Logs after the last full/diff. The LSN test alone misses a log taken right after a diff on an
        // idle database (its LastLsn can equal the diff's), which sp_DatabaseRestore does still apply -
        // it selects logs by backup time - so a log finished after the base counts too.
        var basis = diff ?? baseFull;

        foreach (var log in restorable
                     .Where(e => e.Type == BackupType.Log
                                 && (e.LastLsn >= basis.LastLsn || e.BackupFinishDate > basis.BackupFinishDate))
                     .OrderBy(e => e.FirstLsn))
        {
            steps.Add(log);

            if (target is { } t && log.BackupFinishDate >= t)
            {
                break; // this log covers the target; later logs are not needed
            }
        }

        return new BackupRestorePlan(steps, null);
    }

    private static bool IsBasedOn(BackupFileEntry diff, BackupFileEntry full) =>
        diff.DatabaseBackupLsn is { } baseLsn && full.CheckpointLsn is { } checkpoint
            ? baseLsn == checkpoint
            : diff.BackupFinishDate > full.BackupFinishDate;
}
