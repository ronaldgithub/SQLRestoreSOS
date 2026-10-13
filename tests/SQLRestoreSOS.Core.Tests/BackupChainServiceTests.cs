using SQLRestoreSOS.Core.BackupChain;
using SQLRestoreSOS.Core.BackupChain.Engines;

namespace SQLRestoreSOS.Core.Tests;

public class BackupChainServiceTests
{
    [Theory]
    [InlineData(1, BackupType.Full)]
    [InlineData(5, BackupType.Differential)]
    [InlineData(2, BackupType.Log)]
    public void MapHeaderRow_KnownBackupTypes_MapsCorrectly(short rawType, BackupType expected)
    {
        var entry = DatabaseRestoreEngine.MapHeaderRow(
            @"D:\B\FULL\Orders.bak", rawType, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow, 100m, 200m);

        Assert.NotNull(entry);
        Assert.Equal(expected, entry!.Type);
    }

    [Theory]
    [InlineData(4)]  // File backup
    [InlineData(7)]  // Partial
    [InlineData(8)]  // Differential partial
    public void MapHeaderRow_UnsupportedBackupTypes_ReturnsNull(short rawType)
    {
        var entry = DatabaseRestoreEngine.MapHeaderRow(
            @"D:\B\FULL\Orders.bak", rawType, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow, 100m, 200m);

        Assert.Null(entry);
    }

    [Fact]
    public void BackupChainTimeline_OrdersEntriesByFinishDate()
    {
        var now = DateTime.UtcNow;
        var full = new BackupFileEntry(@"D:\F.bak", BackupType.Full, now.AddHours(-3), now.AddHours(-3), 1, 2);
        var diff = new BackupFileEntry(@"D:\D.bak", BackupType.Differential, now.AddHours(-1), now.AddHours(-1), 2, 3);
        var log = new BackupFileEntry(@"D:\L.trn", BackupType.Log, now.AddMinutes(-10), now.AddMinutes(-10), 3, 4);

        var timeline = new BackupChainTimeline([log, full, diff]); // deliberately out of order

        Assert.Equal(BackupType.Full, timeline.Entries[0].Type);
        Assert.Equal(BackupType.Differential, timeline.Entries[1].Type);
        Assert.Equal(BackupType.Log, timeline.Entries[2].Type);
        Assert.Equal(full, timeline.LatestFull);
        Assert.Single(timeline.DiffsAfter(full.BackupFinishDate));
        Assert.Single(timeline.LogsAfter(diff.BackupFinishDate));
    }

    [Fact]
    public void BackupChainTimeline_NoEntries_LatestAndEarliestAreNull()
    {
        var timeline = new BackupChainTimeline([]);

        Assert.Null(timeline.LatestFull);
        Assert.Null(timeline.EarliestPossiblePointInTime);
        Assert.Null(timeline.LatestPossiblePointInTime);
    }
}
