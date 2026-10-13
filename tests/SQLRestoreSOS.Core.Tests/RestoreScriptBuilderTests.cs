using SQLRestoreSOS.Core.BackupChain.Engines;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.Core.Tests;

public class RestoreScriptBuilderTests
{
    private static readonly DatabaseRestoreEngine Engine = new();

    [Fact]
    public void BuildScript_DefaultsOnly_OmitsNullParameters()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\Backups\Orders\FULL\"
        };

        var parameters = Engine.GetDefaultParameters(request);
        var script = Engine.BuildScript(request, parameters);

        Assert.StartsWith("-- sp_DatabaseRestore (C) Brent Ozar Unlimited", script);
        Assert.Contains("EXEC [dbo].[sp_DatabaseRestore]", script);
        Assert.Contains("@Database = N'Orders'", script);
        Assert.Contains(@"@BackupPathFull = N'D:\Backups\Orders\FULL\'", script);
        Assert.DoesNotContain("@BackupPathDiff", script);
        Assert.DoesNotContain("@StopAt", script);
        Assert.EndsWith(";", script.TrimEnd());
    }

    [Fact]
    public void BuildScript_PointInTimeSet_FormatsAsFourteenDigitString()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\Backups\Orders\FULL\",
            PointInTimeUtc = new DateTime(2026, 3, 5, 14, 30, 5, DateTimeKind.Utc)
        };

        var parameters = Engine.GetDefaultParameters(request);
        var script = Engine.BuildScript(request, parameters);

        Assert.Contains("@StopAt = N'20260305143005'", script);
    }

    [Fact]
    public void BuildScript_MoveFilesSet_IncludesMoveDrives()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\Backups\Orders\FULL\",
            MoveFiles = true,
            MoveDataDrive = @"E:\Data\",
            MoveLogDrive = @"F:\Log\"
        };

        var parameters = Engine.GetDefaultParameters(request);
        var script = Engine.BuildScript(request, parameters);

        Assert.Contains("@MoveFiles = 1", script);
        Assert.Contains(@"@MoveDataDrive = N'E:\Data\'", script);
        Assert.Contains(@"@MoveLogDrive = N'F:\Log\'", script);
    }

    [Fact]
    public void BuildScript_OverwriteSet_SetsExistingDbActionToKillConnections()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\Backups\Orders\FULL\",
            ExistingDatabaseAction = ExistingDatabaseAction.KillConnections
        };

        var parameters = Engine.GetDefaultParameters(request);
        var script = Engine.BuildScript(request, parameters);

        Assert.Contains("@ExistingDBAction = 1", script);
    }

    [Fact]
    public void BuildScript_ExecuteFlag_EmitsYesNoChar()
    {
        var previewRequest = new RestoreRequest { SourceDatabase = "Orders", BackupPathFull = @"D:\B\", ExecuteImmediately = false };
        var runRequest = previewRequest.With(executeImmediately: true);

        var previewScript = Engine.BuildScript(previewRequest, Engine.GetDefaultParameters(previewRequest));
        var runScript = Engine.BuildScript(runRequest, Engine.GetDefaultParameters(runRequest));

        Assert.Contains("@Execute = 'N'", previewScript);
        Assert.Contains("@Execute = 'Y'", runScript);
    }

    [Fact]
    public void BuildScript_SingleQuoteInDatabaseName_IsEscaped()
    {
        var request = new RestoreRequest { SourceDatabase = "O'Brien", BackupPathFull = @"D:\B\" };
        var script = Engine.BuildScript(request, Engine.GetDefaultParameters(request));

        Assert.Contains("@Database = N'O''Brien'", script);
    }

    [Fact]
    public void BuildScript_LogShippingMode_ForcesRunRecoveryFalse()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\B\FULL\",
            BackupPathLog = @"D:\B\LOG\",
            KeepInNoRecoveryForLogShipping = true
        };

        var script = Engine.BuildScript(request, Engine.GetDefaultParameters(request));

        Assert.Contains("@RunRecovery = 0", script);
        Assert.DoesNotContain("@ContinueLogs = 1", script); // the initial log-shipping restore must still restore the FULL
    }
}

file static class RestoreRequestTestExtensions
{
    // RestoreRequest only has init-only setters and no `with` support (it's a class, not a record),
    // so tests build a fresh copy explicitly where a single field needs to differ.
    public static RestoreRequest With(this RestoreRequest source, bool executeImmediately) => new()
    {
        SourceDatabase = source.SourceDatabase,
        TargetDatabase = source.TargetDatabase,
        BackupPathFull = source.BackupPathFull,
        BackupPathDiff = source.BackupPathDiff,
        BackupPathLog = source.BackupPathLog,
        PointInTimeUtc = source.PointInTimeUtc,
        ExistingDatabaseAction = source.ExistingDatabaseAction,
        RunCheckDbAfterRestore = source.RunCheckDbAfterRestore,
        MoveFiles = source.MoveFiles,
        MoveDataDrive = source.MoveDataDrive,
        MoveLogDrive = source.MoveLogDrive,
        ExecuteImmediately = executeImmediately,
        KeepInNoRecoveryForLogShipping = source.KeepInNoRecoveryForLogShipping
    };
}
