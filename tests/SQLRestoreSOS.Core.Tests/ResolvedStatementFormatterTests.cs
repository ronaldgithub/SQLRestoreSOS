using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.Core.Tests;

public class ResolvedStatementFormatterTests
{
    [Fact]
    public void Format_FullBackupAndRecovery_AddsDescriptiveComments()
    {
        var raw =
            "RESTORE DATABASE [StackOverflow2011] FROM DISK = '\\\\srv\\share\\WIN10\\StackOverflow2010\\FULL\\x.bak' WITH NORECOVERY\n\n" +
            "RESTORE DATABASE [StackOverflow2011] WITH RECOVERY";

        var formatted = ResolvedStatementFormatter.Format(raw);

        Assert.Contains("-- Restore FULL backup, NORECOVERY (more backups can still be applied)", formatted);
        Assert.Contains("-- Bring the database online (RECOVERY) - restore chain complete", formatted);
        Assert.Contains("RESTORE DATABASE [StackOverflow2011]\r\nFROM DISK", formatted);
        Assert.Contains("RESTORE DATABASE [StackOverflow2011]\r\nWITH RECOVERY", formatted);
    }

    [Fact]
    public void Format_DiffAndLogBackups_DescribesEachCorrectly()
    {
        var raw =
            "RESTORE DATABASE [Orders] FROM DISK = 'D:\\B\\DIFF\\x.bak' WITH NORECOVERY\n\n" +
            "RESTORE LOG [Orders] FROM DISK = 'D:\\B\\LOG\\y.trn' WITH NORECOVERY";

        var formatted = ResolvedStatementFormatter.Format(raw);

        Assert.Contains("-- Restore DIFFERENTIAL backup", formatted);
        Assert.Contains("-- Apply transaction log backup", formatted);
    }

    [Fact]
    public void Format_NoBlankLineSeparation_SplitsOnRestoreKeyword()
    {
        var raw =
            "RESTORE DATABASE [Orders] FROM DISK = 'D:\\B\\FULL\\x.bak' WITH NORECOVERY\n" +
            "RESTORE DATABASE [Orders] WITH RECOVERY";

        var formatted = ResolvedStatementFormatter.Format(raw);

        Assert.Contains("-- Restore FULL backup", formatted);
        Assert.Contains("-- Bring the database online (RECOVERY) - restore chain complete", formatted);
    }

    [Fact]
    public void Format_EmptyOrWhitespace_ReturnsInputUnchanged()
    {
        Assert.Equal("", ResolvedStatementFormatter.Format(""));
        Assert.Equal("   ", ResolvedStatementFormatter.Format("   "));
    }

    [Fact]
    public void Format_StandbyMode_MentionsStandbyInComment()
    {
        var raw = "RESTORE DATABASE [Orders] FROM DISK = 'D:\\B\\LOG\\y.trn' WITH STANDBY = 'D:\\undo.ldf'";

        var formatted = ResolvedStatementFormatter.Format(raw);

        Assert.Contains("STANDBY (readable, more logs can follow)", formatted);
    }

    [Fact]
    public void Format_MoveClauses_EachOnOwnLine()
    {
        var raw = "RESTORE DATABASE [Orders2011] FROM DISK = 'D:\\B\\FULL\\x.bak' WITH NORECOVERY, " +
                   "MOVE 'Orders' TO 'D:\\databases\\Orders2011.mdf', MOVE 'Orders_log' TO 'D:\\databases\\Orders2011_log.ldf'";

        var formatted = ResolvedStatementFormatter.Format(raw);

        Assert.Contains("RESTORE DATABASE [Orders2011]\r\nFROM DISK", formatted);
        Assert.Contains("\r\nWITH NORECOVERY,\r\n    MOVE 'Orders' TO", formatted);
        Assert.Contains(",\r\n    MOVE 'Orders_log' TO", formatted);
    }

    [Fact]
    public void Format_NoMoveClause_StillBreaksAtFromAndWith()
    {
        var raw = "RESTORE DATABASE [Orders] FROM DISK = 'D:\\B\\FULL\\x.bak' WITH NORECOVERY";

        var formatted = ResolvedStatementFormatter.Format(raw);

        Assert.Contains("RESTORE DATABASE [Orders]\r\nFROM DISK = 'D:\\B\\FULL\\x.bak'\r\nWITH NORECOVERY", formatted);
    }

    [Fact]
    public void Format_MoveClauseWithNoRestorePrefix_FlaggedAsIncomplete()
    {
        // Reproduces an observed sp_DatabaseRestore quirk: the FULL step's statement can print as
        // just its MOVE-clause tail, with the "RESTORE DATABASE ... WITH NORECOVERY, REPLACE"
        // prefix missing from the message stream entirely.
        var raw = ", MOVE 'StackOverflow2010' TO 'D:\\databases\\x.mdf', MOVE 'StackOverflow2010_log' TO 'D:\\databases\\x_log.ldf'";

        var formatted = ResolvedStatementFormatter.Format(raw);

        Assert.Contains("INCOMPLETE STATEMENT", formatted);
        Assert.Contains("MOVE 'StackOverflow2010'", formatted);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void CountStatements_EmptyInput_ReturnsZero(string raw, int expected)
    {
        Assert.Equal(expected, ResolvedStatementFormatter.CountStatements(raw));
    }

    [Fact]
    public void CountStatements_TwoStatements_ReturnsTwo()
    {
        var raw =
            "RESTORE DATABASE [Orders] FROM DISK = 'D:\\B\\DIFF\\x.bak' WITH NORECOVERY\n\n" +
            "RESTORE DATABASE [Orders] WITH RECOVERY";

        Assert.Equal(2, ResolvedStatementFormatter.CountStatements(raw));
    }

    [Fact]
    public void CountStatements_FourStatementChain_ReturnsFour()
    {
        var raw =
            "RESTORE DATABASE [Orders] FROM DISK = 'D:\\B\\FULL\\x.bak' WITH NORECOVERY\n\n" +
            "RESTORE DATABASE [Orders] FROM DISK = 'D:\\B\\DIFF\\y.bak' WITH NORECOVERY\n\n" +
            "RESTORE LOG [Orders] FROM DISK = 'D:\\B\\LOG\\z.trn' WITH NORECOVERY\n\n" +
            "RESTORE DATABASE [Orders] WITH RECOVERY";

        Assert.Equal(4, ResolvedStatementFormatter.CountStatements(raw));
    }
}
