namespace SQLRestoreSOS.Core.BackupChain;

public enum BackupChainStatus
{
    /// <summary>Header was readable and the backup links into a continuous chain - shown as a green check.</summary>
    Restorable,

    /// <summary>Readable, but not part of any chain that can be restored (e.g. older than the earliest full).</summary>
    NotApplicable,

    /// <summary>A link is missing: no base full for a differential, or a gap in the log chain.</summary>
    Broken
}

/// <summary>
/// One analyzed backup. <paramref name="StopAtFrom"/>/<paramref name="StopAtTo"/> are only set for log
/// backups in a continuous chain: the window (server time) a point-in-time STOPAT can fall into when this
/// log is the one being applied last. The end is the log backup's finish time; the start is the finish
/// time of the previous backup in the chain.
/// </summary>
public sealed record BackupChainRow(
    BackupFileEntry Entry,
    BackupChainStatus Status,
    string Note,
    DateTime? StopAtFrom,
    DateTime? StopAtTo)
{
    public bool IsRestorable => Status == BackupChainStatus.Restorable;
}

/// <summary>
/// Pure, offline chain analysis (LSN continuity) behind the timeline dialog's green checks. This is
/// header/LSN reasoning only - it does not run RESTORE VERIFYONLY, so a corrupt backup body is not detected.
/// </summary>
public static class BackupChainAnalyzer
{
    public static IReadOnlyList<BackupChainRow> Analyze(BackupChainTimeline timeline)
    {
        var results = new Dictionary<BackupFileEntry, BackupChainRow>();
        var fulls = timeline.Entries.Where(e => e.Type == BackupType.Full).ToList();
        var latestFullFinish = timeline.LatestFull?.BackupFinishDate;

        foreach (var full in fulls)
        {
            results[full] = new BackupChainRow(full, BackupChainStatus.Restorable, "Full backup - can be restored on its own.", null, null);
        }

        var restorableDiffs = new List<BackupFileEntry>();
        foreach (var diff in timeline.Entries.Where(e => e.Type == BackupType.Differential))
        {
            var baseFull = FindBaseFull(diff, fulls);
            if (baseFull is null)
            {
                results[diff] = new BackupChainRow(diff, BackupChainStatus.Broken, "Base full backup not found.", null, null);
                continue;
            }

            restorableDiffs.Add(diff);
            results[diff] = new BackupChainRow(
                diff, BackupChainStatus.Restorable,
                $"Based on full of {baseFull.BackupFinishDate:yyyy-MM-dd HH:mm:ss}.", null, null);
        }

        AnalyzeLogs(timeline, fulls, restorableDiffs, latestFullFinish, results);

        return timeline.Entries.Select(e => results[e]).ToList();
    }

    private static BackupFileEntry? FindBaseFull(BackupFileEntry diff, List<BackupFileEntry> fulls)
    {
        if (diff.DatabaseBackupLsn is { } baseLsn)
        {
            return fulls.FirstOrDefault(f => f.CheckpointLsn == baseLsn);
        }

        // No LSN info (e.g. msdb-sourced entry): fall back to the newest full finished before the diff.
        return fulls.Where(f => f.BackupFinishDate <= diff.BackupFinishDate)
                    .OrderByDescending(f => f.BackupFinishDate)
                    .FirstOrDefault();
    }

    private static void AnalyzeLogs(
        BackupChainTimeline timeline,
        List<BackupFileEntry> fulls,
        List<BackupFileEntry> restorableDiffs,
        DateTime? latestFullFinish,
        Dictionary<BackupFileEntry, BackupChainRow> results)
    {
        var bases = fulls.Concat(restorableDiffs).ToList();
        var minBaseLastLsn = bases.Count == 0 ? (decimal?)null : bases.Min(b => b.LastLsn);

        decimal? currentLsn = null;
        DateTime? previousFinish = null;

        var logs = timeline.Entries
            .Where(e => e.Type == BackupType.Log)
            .OrderBy(e => e.FirstLsn)
            .ThenBy(e => e.BackupFinishDate);

        foreach (var log in logs)
        {
            if (currentLsn is { } current && log.FirstLsn <= current)
            {
                // Continues the chain (or is already covered by it).
                currentLsn = Math.Max(current, log.LastLsn);
                results[log] = Linked(log, previousFinish, latestFullFinish);
                previousFinish = log.BackupFinishDate;
                continue;
            }

            // Start of a chain, or a gap: a base whose LastLsn falls inside this log can seed it.
            var seed = bases
                .Where(b => b.LastLsn >= log.FirstLsn && b.LastLsn < log.LastLsn)
                .OrderBy(b => b.LastLsn)
                .FirstOrDefault();

            if (seed is not null)
            {
                currentLsn = log.LastLsn;
                previousFinish = seed.BackupFinishDate;
                results[log] = Linked(log, previousFinish, latestFullFinish);
                previousFinish = log.BackupFinishDate;
            }
            else if (currentLsn is null && minBaseLastLsn is { } minBase && log.LastLsn <= minBase)
            {
                results[log] = new BackupChainRow(
                    log, BackupChainStatus.NotApplicable,
                    "Older than the earliest full backup - not part of any chain.", null, null);
            }
            else if (currentLsn is null)
            {
                results[log] = new BackupChainRow(log, BackupChainStatus.Broken, "No full or differential backup links to this log.", null, null);
            }
            else
            {
                results[log] = new BackupChainRow(
                    log, BackupChainStatus.Broken,
                    "Gap: a log backup between the previous one and this one is missing.", null, null);
            }
        }
    }

    private static BackupChainRow Linked(BackupFileEntry log, DateTime? previousFinish, DateTime? latestFullFinish)
    {
        var note = latestFullFinish is { } fullFinish && log.BackupFinishDate <= fullFinish
            ? "Older than the latest full - only needed when restoring from an older full."
            : "Linked into the chain.";

        return new BackupChainRow(log, BackupChainStatus.Restorable, note, previousFinish, log.BackupFinishDate);
    }
}
