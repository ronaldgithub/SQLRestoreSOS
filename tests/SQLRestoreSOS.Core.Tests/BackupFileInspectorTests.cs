using SQLRestoreSOS.Core.BackupChain;

namespace SQLRestoreSOS.Core.Tests;

public class BackupFileInspectorTests
{
    [Fact]
    public void MapFileListRow_TypicalDataFile_MapsAllFields()
    {
        var entry = BackupFileInspector.MapFileListRow("Orders", @"D:\Data\Orders.mdf", "D", "PRIMARY", 104857600, 0);

        Assert.Equal("Orders", entry.LogicalName);
        Assert.Equal(@"D:\Data\Orders.mdf", entry.PhysicalName);
        Assert.Equal('D', entry.Type);
        Assert.Equal("PRIMARY", entry.FileGroupName);
        Assert.Equal(104857600, entry.Size);
    }

    [Fact]
    public void MapFileListRow_LogFile_HasNullFileGroup()
    {
        var entry = BackupFileInspector.MapFileListRow("Orders_log", @"D:\Log\Orders_log.ldf", "L", null, 52428800, 0);

        Assert.Equal('L', entry.Type);
        Assert.Null(entry.FileGroupName);
    }

    [Fact]
    public void MapFileListRow_LowercaseType_IsNormalizedToUppercase()
    {
        var entry = BackupFileInspector.MapFileListRow("Orders", @"D:\Data\Orders.mdf", "d", "PRIMARY", 100, 0);

        Assert.Equal('D', entry.Type);
    }
}
