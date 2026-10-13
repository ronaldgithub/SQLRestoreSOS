using SQLRestoreSOS.Core.BackupChain.Engines;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.Core.Tests;

public class RestoreParameterDefaultsTests
{
    private static readonly DatabaseRestoreEngine Engine = new();

    [Fact]
    public void GetDefaultParameters_MinimalRequest_IsSafeByDefault()
    {
        var request = new RestoreRequest { SourceDatabase = "Orders", BackupPathFull = @"D:\B\FULL\" };
        var p = Engine.GetDefaultParameters(request);

        Assert.False(p.GetBool("Execute"), "Execute must default to false (preview-only) until the DBA explicitly clicks Run.");
        Assert.Equal(0, p.GetInt("ExistingDBAction"));
        Assert.False(p.GetBool("RunCheckDB"));
        Assert.True(p.GetBool("RunRecovery"));
    }

    [Fact]
    public void GetDefaultParameters_NoDiffOrLogPathGiven_DoesNotEnableThem()
    {
        var request = new RestoreRequest { SourceDatabase = "Orders", BackupPathFull = @"D:\B\FULL\" };
        var p = Engine.GetDefaultParameters(request);

        Assert.False(p.GetBool("RestoreDiff"));
        Assert.False(p.GetBool("ContinueLogs"));
    }

    [Fact]
    public void GetDefaultParameters_DiffAndLogPathGiven_RestoresDiffButDoesNotContinueLogs()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\B\FULL\",
            BackupPathDiff = @"D:\B\DIFF\",
            BackupPathLog = @"D:\B\LOG\"
        };
        var p = Engine.GetDefaultParameters(request);

        Assert.True(p.GetBool("RestoreDiff"));
        Assert.Equal(@"D:\B\LOG\", p.Get("BackupPathLog"));
        // @ContinueLogs = 1 would make the proc skip the FULL restore - logs are applied from
        // @BackupPathLog alone.
        Assert.False(p.GetBool("ContinueLogs"));
    }

    [Fact]
    public void GetDefaultParameters_ContinueExistingRestore_ContinuesLogsAndSkipsDiff()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\B\FULL\",
            BackupPathDiff = @"D:\B\DIFF\",
            BackupPathLog = @"D:\B\LOG\",
            ContinueExistingRestore = true
        };
        var p = Engine.GetDefaultParameters(request);

        Assert.True(p.GetBool("ContinueLogs"));
        Assert.False(p.GetBool("RestoreDiff"));
        Assert.Null(p.Get("BackupPathDiff"));
    }

    [Fact]
    public void GetDefaultParameters_LogShippingMode_ForcesNoRecoveryRegardlessOfOtherFlags()
    {
        var request = new RestoreRequest
        {
            SourceDatabase = "Orders",
            BackupPathFull = @"D:\B\FULL\",
            BackupPathLog = @"D:\B\LOG\",
            KeepInNoRecoveryForLogShipping = true
        };
        var p = Engine.GetDefaultParameters(request);

        Assert.False(p.GetBool("RunRecovery"));
    }

    [Theory]
    [InlineData(ExistingDatabaseAction.DoNothing, 0)]
    [InlineData(ExistingDatabaseAction.KillConnections, 1)]
    [InlineData(ExistingDatabaseAction.TakeOffline, 2)]
    [InlineData(ExistingDatabaseAction.Drop, 3)]
    public void GetDefaultParameters_ExistingDatabaseAction_MapsToProcTinyintValue(ExistingDatabaseAction action, int expected)
    {
        var request = new RestoreRequest { SourceDatabase = "Orders", BackupPathFull = @"D:\B\", ExistingDatabaseAction = action };
        var p = Engine.GetDefaultParameters(request);

        Assert.Equal(expected, p.GetInt("ExistingDBAction"));
    }

    [Fact]
    public void DescribeParameters_CoversAllGroups()
    {
        var descriptors = Engine.DescribeParameters();

        Assert.NotEmpty(descriptors);
        foreach (RestoreParameterGroup group in Enum.GetValues<RestoreParameterGroup>())
        {
            Assert.Contains(descriptors, d => d.Group == group);
        }
    }
}
