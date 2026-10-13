using SQLRestoreSOS.Core.BackupChain;

namespace SQLRestoreSOS.Core.Tests;

public class BackupPathDiagnosticsTests
{
    [Theory]
    [InlineData(@"W:\WIN10\StackOverflow2010\FULL\")]
    [InlineData(@"D:\Backups\Orders\FULL\")]
    [InlineData(@"z:\backups")]
    public void IsMappedDriveLetterPath_DriveLetterPaths_ReturnsTrue(string path)
    {
        Assert.True(BackupPathDiagnostics.IsMappedDriveLetterPath(path));
    }

    [Theory]
    [InlineData(@"\\192.168.178.15\SQLBackup\WIN10\StackOverflow2010\FULL\")]
    [InlineData(@"\\srv\share")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsMappedDriveLetterPath_UncOrEmptyPaths_ReturnsFalse(string? path)
    {
        Assert.False(BackupPathDiagnostics.IsMappedDriveLetterPath(path));
    }
}
