using SQLRestoreSOS.Core.BackupChain;

namespace SQLRestoreSOS.Core.Tests;

public class BackupChainAnalyzerTests
{
    private static readonly DateTime T0 = new(2026, 10, 13, 8, 0, 0);

    private static BackupFileEntry Full(int minute, decimal first, decimal last, decimal checkpoint) =>
        new($@"D:\FULL\{minute}.bak", BackupType.Full, T0.AddMinutes(minute), T0.AddMinutes(minute + 1), first, last, 0, checkpoint);

    private static BackupFileEntry Diff(int minute, decimal first, decimal last, decimal baseCheckpoint) =>
        new($@"D:\DIFF\{minute}.bak", BackupType.Differential, T0.AddMinutes(minute), T0.AddMinutes(minute + 1), first, last, baseCheckpoint, last);

    private static BackupFileEntry Log(int minute, decimal first, decimal last) =>
        new($@"D:\LOG\{minute}.trn", BackupType.Log, T0.AddMinutes(minute), T0.AddMinutes(minute + 1), first, last);

    private static BackupChainRow Row(IReadOnlyList<BackupChainRow> rows, BackupFileEntry e) => rows.Single(r => r.Entry == e);

    [Fact]
    public void ContinuousChain_AllRestorable_WithStopAtWindowsChainedByFinishTime()
    {
        var full = Full(0, 100, 200, 190);
        var log1 = Log(10, 150, 300);
        var log2 = Log(20, 300, 400);

        var rows = BackupChainAnalyzer.Analyze(new BackupChainTimeline([full, log1, log2]));

        Assert.All(rows, r => Assert.True(r.IsRestorable));

        var r1 = Row(rows, log1);
        Assert.Equal(full.BackupFinishDate, r1.StopAtFrom);
        Assert.Equal(log1.BackupFinishDate, r1.StopAtTo);

        var r2 = Row(rows, log2);
        Assert.Equal(log1.BackupFinishDate, r2.StopAtFrom);
        Assert.Equal(log2.BackupFinishDate, r2.StopAtTo);
    }

    [Fact]
    public void MissingLog_MarksFollowingLogBroken()
    {
        var full = Full(0, 100, 200, 190);
        var log1 = Log(10, 150, 300);
        var log3 = Log(30, 500, 600); // 300..500 missing

        var rows = BackupChainAnalyzer.Analyze(new BackupChainTimeline([full, log1, log3]));

        Assert.True(Row(rows, log1).IsRestorable);
        var broken = Row(rows, log3);
        Assert.Equal(BackupChainStatus.Broken, broken.Status);
        Assert.Null(broken.StopAtFrom);
        Assert.Null(broken.StopAtTo);
    }

    [Fact]
    public void LogOlderThanEarliestFull_IsNotApplicableNotBroken()
    {
        var oldLog = Log(0, 10, 50);
        var full = Full(10, 100, 200, 190);
        var log = Log(20, 150, 300);

        var rows = BackupChainAnalyzer.Analyze(new BackupChainTimeline([oldLog, full, log]));

        Assert.Equal(BackupChainStatus.NotApplicable, Row(rows, oldLog).Status);
        Assert.True(Row(rows, log).IsRestorable);
    }

    [Fact]
    public void Differential_WithoutMatchingBaseFull_IsBroken()
    {
        var full = Full(0, 100, 200, 190);
        var orphanDiff = Diff(10, 500, 600, baseCheckpoint: 999);

        var rows = BackupChainAnalyzer.Analyze(new BackupChainTimeline([full, orphanDiff]));

        Assert.Equal(BackupChainStatus.Broken, Row(rows, orphanDiff).Status);
    }

    [Fact]
    public void Differential_MatchingBaseFull_IsRestorableAndCanSeedLogs()
    {
        var full = Full(0, 100, 200, 190);
        var diff = Diff(10, 250, 400, baseCheckpoint: 190);
        var log = Log(20, 380, 500);

        var rows = BackupChainAnalyzer.Analyze(new BackupChainTimeline([full, diff, log]));

        Assert.True(Row(rows, diff).IsRestorable);
        Assert.True(Row(rows, log).IsRestorable);
    }

    [Fact]
    public void Rows_AreInBackupFinishOrder()
    {
        var full = Full(0, 100, 200, 190);
        var log = Log(10, 150, 300);

        var rows = BackupChainAnalyzer.Analyze(new BackupChainTimeline([log, full]));

        Assert.Equal([full, log], rows.Select(r => r.Entry).ToArray());
    }
}

public class BackupRestorePlannerTests
{
    private static readonly DateTime T0 = new(2026, 10, 13, 8, 0, 0);

    private static BackupFileEntry Entry(string name, BackupType type, int startMin, int finishMin, decimal first, decimal last, decimal? dbLsn = null, decimal? ckpt = null) =>
        new(name, type, T0.AddMinutes(startMin), T0.AddMinutes(finishMin), first, last, dbLsn, ckpt);

    private static IReadOnlyList<BackupChainRow> Rows(params BackupFileEntry[] entries) =>
        BackupChainAnalyzer.Analyze(new BackupChainTimeline(entries));

    // Mirrors the real case: full 08:27-08:28, log 08:45, log 09:00, diff 09:06, log 09:06.
    private static readonly BackupFileEntry Full = Entry("full", BackupType.Full, 27, 28, 100, 200, 0, 190);
    private static readonly BackupFileEntry Log1 = Entry("log1", BackupType.Log, 45, 45, 150, 300);
    private static readonly BackupFileEntry Log2 = Entry("log2", BackupType.Log, 60, 60, 300, 400);
    private static readonly BackupFileEntry Diff = Entry("diff", BackupType.Differential, 66, 66, 250, 410, 190, 410);
    private static readonly BackupFileEntry Log3 = Entry("log3", BackupType.Log, 67, 67, 400, 500);

    [Fact]
    public void TargetInsideFirstLog_AppliesFullAndThatLogOnly()
    {
        var plan = BackupRestorePlanner.Plan(Rows(Full, Log1, Log2, Diff, Log3), T0.AddMinutes(40));

        Assert.Null(plan.Problem);
        Assert.Equal([Full, Log1], plan.Steps.ToArray());
    }

    [Fact]
    public void NullTarget_AppliesFullDiffAndLaterLogs()
    {
        var plan = BackupRestorePlanner.Plan(Rows(Full, Log1, Log2, Diff, Log3), null);

        Assert.Equal([Full, Diff, Log3], plan.Steps.ToArray());
    }

    [Fact]
    public void TargetBeforeFullFinished_HasProblemAndNoSteps()
    {
        var plan = BackupRestorePlanner.Plan(Rows(Full, Log1), T0.AddMinutes(27).AddSeconds(30));

        Assert.NotNull(plan.Problem);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void TargetAfterDiff_UsesDiffThenLogsAfterIt()
    {
        var plan = BackupRestorePlanner.Plan(Rows(Full, Log1, Log2, Diff, Log3), T0.AddMinutes(67));

        Assert.Equal([Full, Diff, Log3], plan.Steps.ToArray());
    }

    [Fact]
    public void LogTakenRightAfterDiff_WithLastLsnAtOrBelowDiff_IsStillApplied()
    {
        // Idle database: the log taken seconds after the diff ends at the diff's own end LSN.
        var logAfterDiff = Entry("logAfterDiff", BackupType.Log, 66, 67, 300, 410);
        var laterLog = Entry("later", BackupType.Log, 90, 90, 410, 600);

        var plan = BackupRestorePlanner.Plan(Rows(Full, Log1, Log2, Diff, logAfterDiff, laterLog), T0.AddMinutes(90));

        Assert.Equal([Full, Diff, logAfterDiff, laterLog], plan.Steps.ToArray());
    }
}
