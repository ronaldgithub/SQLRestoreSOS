using SQLRestoreSOS.Core.BackupChain;

namespace SQLRestoreSOS.Core.Tests;

public class OlaHallengrenBackupPathsTests
{
    [Fact]
    public void Build_DefaultInstance_ProducesExpectedLayout()
    {
        var (full, diff, log) = OlaHallengrenBackupPaths.Build(@"\\192.168.178.15\SQLBackup", "WIN10", "StackOverflow2010");

        Assert.Equal(@"\\192.168.178.15\SQLBackup\WIN10\StackOverflow2010\FULL\", full);
        Assert.Equal(@"\\192.168.178.15\SQLBackup\WIN10\StackOverflow2010\DIFF\", diff);
        Assert.Equal(@"\\192.168.178.15\SQLBackup\WIN10\StackOverflow2010\LOG\", log);
    }

    [Fact]
    public void Build_NamedInstance_SanitizesBackslashToDollar()
    {
        var (full, _, _) = OlaHallengrenBackupPaths.Build(@"D:\Backups", @"WIN10\SQLEXPRESS", "Orders");

        Assert.Equal(@"D:\Backups\WIN10$SQLEXPRESS\Orders\FULL\", full);
    }

    [Fact]
    public void Build_RootWithTrailingSlash_DoesNotDoubleSlash()
    {
        var (full, _, _) = OlaHallengrenBackupPaths.Build(@"\\srv\share\", "WIN10", "Orders");

        Assert.Equal(@"\\srv\share\WIN10\Orders\FULL\", full);
    }

    [Fact]
    public void Build_RootAlreadyPointsAtDatabaseFolder_DoesNotDuplicatePath()
    {
        // Real-world repro: the DBA browsed Explorer straight to the per-database folder (it's
        // the obvious thing to pick - FULL/DIFF/LOG are visibly right there) instead of the share
        // root, and connected via "localhost" while the on-disk folder uses the real machine name.
        var (full, diff, log) = OlaHallengrenBackupPaths.Build(@"W:\WIN10\StackOverflow2010", "localhost", "StackOverflow2010");

        Assert.Equal(@"W:\WIN10\StackOverflow2010\FULL\", full);
        Assert.Equal(@"W:\WIN10\StackOverflow2010\DIFF\", diff);
        Assert.Equal(@"W:\WIN10\StackOverflow2010\LOG\", log);
    }

    [Fact]
    public void Build_RootAlreadyPointsAtDatabaseFolder_MatchingServerName_StillWorks()
    {
        var (full, _, _) = OlaHallengrenBackupPaths.Build(@"\\192.168.178.15\SQLBackup\WIN10\StackOverflow2010", "WIN10", "StackOverflow2010");

        Assert.Equal(@"\\192.168.178.15\SQLBackup\WIN10\StackOverflow2010\FULL\", full);
    }

    [Fact]
    public void Build_RootEndsWithDifferentDatabaseName_StillAppendsInstanceAndDatabase()
    {
        // The root happens to end in a folder name, but it's not THIS database - must not be
        // mistaken for an already-complete path.
        var (full, _, _) = OlaHallengrenBackupPaths.Build(@"\\srv\share\SomeOtherFolder", "WIN10", "Orders");

        Assert.Equal(@"\\srv\share\SomeOtherFolder\WIN10\Orders\FULL\", full);
    }
}
