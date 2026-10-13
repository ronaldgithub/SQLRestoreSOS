using System.Text;
using Microsoft.Data.SqlClient;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.Core.BackupChain.Engines;

/// <summary>
/// Primary/bundled engine: adapts sp_DatabaseRestore (Brent Ozar Unlimited / FirstResponderKit.org, MIT).
/// Discovers backups directly from filesystem folders (resilient even if msdb history is gone),
/// and can both execute a real restore and print resolved commands without running them.
/// </summary>
public sealed class DatabaseRestoreEngine : IRestoreEngine
{
    private readonly Func<CancellationToken, Task<SqlConnection>>? _secondaryConnectionFactory;

    /// <param name="secondaryConnectionFactory">
    /// Optional: opens a second connection used only to poll sys.dm_exec_requests.percent_complete
    /// while a restore is running on the primary connection. Omit to skip percent-complete polling.
    /// </param>
    public DatabaseRestoreEngine(Func<CancellationToken, Task<SqlConnection>>? secondaryConnectionFactory = null)
    {
        _secondaryConnectionFactory = secondaryConnectionFactory;
    }

    public string Name => "sp_DatabaseRestore";

    /// <summary>True if sp_DatabaseRestore is installed on the connected server (it installs into [master] by default).</summary>
    public static async Task<bool> IsInstalledAsync(SqlConnection conn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT OBJECT_ID('master.dbo.sp_DatabaseRestore');", conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null and not DBNull;
    }

    /// <summary>
    /// True if Ola Hallengren's dbo.CommandExecute exists in [master] - sp_DatabaseRestore requires
    /// it to actually run any RESTORE command and raises its own error otherwise, but checking here
    /// lets the UI surface a clear "install it first" message at Connect time instead.
    /// </summary>
    public static async Task<bool> IsCommandExecuteDependencyInstalledAsync(SqlConnection conn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT OBJECT_ID('master.dbo.CommandExecute');", conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null and not DBNull;
    }

    public RestoreParameterSet GetDefaultParameters(RestoreRequest request)
    {
        var p = new RestoreParameterSet();

        p.Set("Database", request.SourceDatabase);
        p.Set("RestoreDatabaseName", request.TargetDatabaseOrDefault);
        p.Set("ExistingDBAction", (int)request.ExistingDatabaseAction);

        p.Set("BackupPathFull", request.BackupPathFull);

        // When continuing an existing NORECOVERY restore the DIFF is already applied - the proc
        // re-restores it whenever @RestoreDiff = 1, regardless of @ContinueLogs, so leave it out.
        var restoreDiff = !request.ContinueExistingRestore && !string.IsNullOrWhiteSpace(request.BackupPathDiff);
        if (restoreDiff)
        {
            p.Set("BackupPathDiff", request.BackupPathDiff);
        }

        if (!string.IsNullOrWhiteSpace(request.BackupPathLog))
        {
            p.Set("BackupPathLog", request.BackupPathLog);
        }

        if (request.PointInTimeUtc is { } stopAt)
        {
            p.Set("StopAt", stopAt);
        }

        p.Set("RestoreDiff", restoreDiff);

        // @ContinueLogs does NOT mean "also apply logs" (the proc applies logs whenever
        // @BackupPathLog is set) - it means "skip the FULL restore, the database is already
        // restoring". Only a log-shipping follow-up step may set it.
        p.Set("ContinueLogs", request.ContinueExistingRestore);

        if (request.MoveFiles)
        {
            p.Set("MoveFiles", true);
            if (!string.IsNullOrWhiteSpace(request.MoveDataDrive))
            {
                p.Set("MoveDataDrive", request.MoveDataDrive);
            }

            if (!string.IsNullOrWhiteSpace(request.MoveLogDrive))
            {
                p.Set("MoveLogDrive", request.MoveLogDrive);
            }
        }

        // Log-shipping mode always stays in NORECOVERY, regardless of point-in-time, so
        // LogShippingSession can keep applying newly-arrived log backups later.
        p.Set("RunRecovery", !request.KeepInNoRecoveryForLogShipping);

        p.Set("RunCheckDB", request.RunCheckDbAfterRestore);
        p.Set("Execute", request.ExecuteImmediately);
        p.Set("Debug", 0);

        return p;
    }

    public IReadOnlyList<RestoreParameterDescriptor> DescribeParameters() => s_descriptors;

    public string BuildScript(RestoreRequest request, RestoreParameterSet parameters)
    {
        var ordered = new (string Name, RestoreParameterKind Kind, object? Value)[]
        {
            ("Database", RestoreParameterKind.String, parameters.Get("Database")),
            ("RestoreDatabaseName", RestoreParameterKind.String, parameters.Get("RestoreDatabaseName")),
            ("BackupPathFull", RestoreParameterKind.Path, parameters.Get("BackupPathFull")),
            ("BackupPathDiff", RestoreParameterKind.Path, parameters.Get("BackupPathDiff")),
            ("BackupPathLog", RestoreParameterKind.Path, parameters.Get("BackupPathLog")),
            ("MoveFiles", RestoreParameterKind.Bit, parameters.Get("MoveFiles")),
            ("MoveDataDrive", RestoreParameterKind.Path, parameters.Get("MoveDataDrive")),
            ("MoveLogDrive", RestoreParameterKind.Path, parameters.Get("MoveLogDrive")),
            ("MoveFilestreamDrive", RestoreParameterKind.Path, parameters.Get("MoveFilestreamDrive")),
            ("MoveFullTextCatalogDrive", RestoreParameterKind.Path, parameters.Get("MoveFullTextCatalogDrive")),
            ("TestRestore", RestoreParameterKind.Bit, parameters.Get("TestRestore")),
            ("RunCheckDB", RestoreParameterKind.Bit, parameters.Get("RunCheckDB")),
            ("RestoreDiff", RestoreParameterKind.Bit, parameters.Get("RestoreDiff")),
            ("ContinueLogs", RestoreParameterKind.Bit, parameters.Get("ContinueLogs")),
            ("StandbyMode", RestoreParameterKind.Bit, parameters.Get("StandbyMode")),
            ("StandbyUndoPath", RestoreParameterKind.Path, parameters.Get("StandbyUndoPath")),
            ("RunRecovery", RestoreParameterKind.Bit, parameters.Get("RunRecovery")),
            ("ForceSimpleRecovery", RestoreParameterKind.Bit, parameters.Get("ForceSimpleRecovery")),
            ("ExistingDBAction", RestoreParameterKind.Int, parameters.Get("ExistingDBAction")),
            ("StopAt", RestoreParameterKind.DateTime, parameters.Get("StopAt")),
            ("OnlyLogsAfter", RestoreParameterKind.DateTime, parameters.Get("OnlyLogsAfter")),
            ("SimpleFolderEnumeration", RestoreParameterKind.Bit, parameters.Get("SimpleFolderEnumeration")),
            ("SkipBackupsAlreadyInMsdb", RestoreParameterKind.Bit, parameters.Get("SkipBackupsAlreadyInMsdb")),
            ("DatabaseOwner", RestoreParameterKind.String, parameters.Get("DatabaseOwner")),
            ("SetTrustworthyON", RestoreParameterKind.Bit, parameters.Get("SetTrustworthyON")),
            ("FixOrphanUsers", RestoreParameterKind.Bit, parameters.Get("FixOrphanUsers")),
            ("KeepCdc", RestoreParameterKind.Bit, parameters.Get("KeepCdc")),
            ("Execute", RestoreParameterKind.YesNoChar, parameters.Get("Execute")),
            ("FileExtensionBak", RestoreParameterKind.String, parameters.Get("FileExtensionBak")),
            ("FileExtensionDiff", RestoreParameterKind.String, parameters.Get("FileExtensionDiff")),
            ("Debug", RestoreParameterKind.Int, parameters.Get("Debug")),
            ("FileNamePrefix", RestoreParameterKind.String, parameters.Get("FileNamePrefix")),
            ("RunStoredProcAfterRestore", RestoreParameterKind.String, parameters.Get("RunStoredProcAfterRestore")),
            ("EnableBroker", RestoreParameterKind.Bit, parameters.Get("EnableBroker")),
            ("BufferCount", RestoreParameterKind.Int, parameters.Get("BufferCount")),
            ("MaxTransferSize", RestoreParameterKind.Int, parameters.Get("MaxTransferSize")),
            ("BlockSize", RestoreParameterKind.Int, parameters.Get("BlockSize")),
        };

        return RestoreScriptBuilder.BuildExecCall(
            "[dbo].[sp_DatabaseRestore]",
            ordered,
            headerComment: "sp_DatabaseRestore (C) Brent Ozar Unlimited / FirstResponderKit.org - MIT License - http://FirstResponderKit.org");
    }

    public async Task ExecuteAsync(
        SqlConnection conn,
        RestoreRequest request,
        RestoreParameterSet parameters,
        IProgress<RestoreProgressEvent>? progress,
        CancellationToken ct = default)
    {
        var script = BuildScript(request, parameters);
        var executing = parameters.GetBool("Execute");

        SqlInfoMessageEventHandler handler = (_, e) =>
        {
            foreach (SqlError err in e.Errors)
            {
                var severity = err.Class >= 11 ? RestoreProgressSeverity.Error : RestoreProgressSeverity.Info;
                progress?.Report(new RestoreProgressEvent(DateTime.UtcNow, err.Message, severity));
            }
        };

        conn.InfoMessage += handler;
        IDisposable? poller = null;
        try
        {
            if (executing)
            {
                poller = StartPercentCompletePoller(progress, ct);
            }

            await using var cmd = new SqlCommand(script, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            progress?.Report(new RestoreProgressEvent(
                DateTime.UtcNow,
                executing ? "Restore completed." : "Preview resolved - no data was changed.",
                RestoreProgressSeverity.Info));
        }
        finally
        {
            conn.InfoMessage -= handler;
            poller?.Dispose();
        }
    }

    public async Task<string> ResolveActualStatementsAsync(
        SqlConnection conn,
        RestoreRequest request,
        RestoreParameterSet parameters,
        CancellationToken ct = default)
    {
        // Force a dry run regardless of the caller's Execute flag - sp_DatabaseRestore prints the
        // resolved RESTORE DATABASE/RESTORE LOG commands as info messages when @Execute = 'N'
        // instead of running them, which is exactly the "-WhatIf" behavior being surfaced here.
        var previewParameters = parameters.Clone();
        previewParameters.Set("Execute", false);
        var script = BuildScript(request, previewParameters);

        var sb = new StringBuilder();
        SqlInfoMessageEventHandler handler = (_, e) =>
        {
            foreach (SqlError err in e.Errors)
            {
                sb.AppendLine(err.Message);
            }
        };

        conn.InfoMessage += handler;
        try
        {
            await using var cmd = new SqlCommand(script, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            conn.InfoMessage -= handler;
        }

        return sb.ToString().TrimEnd();
    }

    public async Task<BackupChainTimeline> DiscoverBackupChainAsync(
        SqlConnection conn,
        BackupChainQuery query,
        CancellationToken ct = default)
    {
        var folders = new[] { query.BackupPathFull, query.BackupPathDiff, query.BackupPathLog }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var entries = new List<BackupFileEntry>();

        foreach (var folder in folders)
        {
            List<string> files;
            try
            {
                files = await EnumerateFilesAsync(conn, folder, ct).ConfigureAwait(false);
            }
            catch (SqlException)
            {
                // Folder missing/unreadable from the server - skip; a real restore attempt will
                // surface this clearly via the proc's own RAISERROR messages.
                continue;
            }

            foreach (var file in files)
            {
                var entry = await TryReadHeaderAsync(conn, file, ct).ConfigureAwait(false);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        return new BackupChainTimeline(entries);
    }

    /// <summary>Finds log backups newer than <paramref name="afterFinishDateUtc"/> - used by LogShippingSession.</summary>
    public async Task<IReadOnlyList<BackupFileEntry>> FindNewLogBackupsAsync(
        SqlConnection conn,
        string backupPathLog,
        DateTime afterFinishDateUtc,
        CancellationToken ct = default)
    {
        var timeline = await DiscoverBackupChainAsync(
            conn,
            new BackupChainQuery(string.Empty, null, null, backupPathLog),
            ct).ConfigureAwait(false);

        return timeline.Entries
            .Where(e => e.Type == BackupType.Log && e.BackupFinishDate > afterFinishDateUtc)
            .OrderBy(e => e.BackupFinishDate)
            .ToList();
    }

    private static async Task<List<string>> EnumerateFilesAsync(SqlConnection conn, string folderPath, CancellationToken ct)
    {
        const string sql = "EXEC master.sys.xp_dirtree @path, 1, 1;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@path", folderPath);

        var files = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var isFile = reader.GetInt32(2) == 1; // xp_dirtree columns: subdirectory, depth, file
            if (isFile)
            {
                files.Add(folderPath.TrimEnd('\\', '/') + "\\" + name);
            }
        }

        return files;
    }

    private static async Task<BackupFileEntry?> TryReadHeaderAsync(SqlConnection conn, string filePath, CancellationToken ct)
    {
        const string sql = "RESTORE HEADERONLY FROM DISK = @path;";
        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@path", filePath);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            return MapHeaderRow(
                filePath,
                backupType: Convert.ToInt16(reader["BackupType"]),
                backupStartDate: Convert.ToDateTime(reader["BackupStartDate"]),
                backupFinishDate: Convert.ToDateTime(reader["BackupFinishDate"]),
                firstLsn: Convert.ToDecimal(reader["FirstLSN"]),
                lastLsn: Convert.ToDecimal(reader["LastLSN"]));
        }
        catch (SqlException)
        {
            // Not a readable backup file (wrong format, permissions, in-use, etc.) - skip it silently;
            // this is a best-effort discovery scan, not a validation step.
            return null;
        }
    }

    /// <summary>Pure mapping logic, kept separate from ADO.NET so it is unit-testable with fake header values.</summary>
    internal static BackupFileEntry? MapHeaderRow(
        string filePath,
        short backupType,
        DateTime backupStartDate,
        DateTime backupFinishDate,
        decimal firstLsn,
        decimal lastLsn)
    {
        // RESTORE HEADERONLY's BackupType: 1 = Database (Full), 2 = Transaction Log, 5 = Differential database.
        // Other values (file, partial, differential-file/partial backups) are out of scope for this app.
        BackupType? mapped = backupType switch
        {
            1 => BackupType.Full,
            5 => BackupType.Differential,
            2 => BackupType.Log,
            _ => null
        };

        return mapped is null ? null : new BackupFileEntry(filePath, mapped.Value, backupStartDate, backupFinishDate, firstLsn, lastLsn);
    }

    private IDisposable? StartPercentCompletePoller(IProgress<RestoreProgressEvent>? progress, CancellationToken ct)
    {
        if (_secondaryConnectionFactory is null || progress is null)
        {
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cts.Token).ConfigureAwait(false);

                    try
                    {
                        await using var secondary = await _secondaryConnectionFactory(cts.Token).ConfigureAwait(false);
                        await using var cmd = new SqlCommand(
                            "SELECT TOP (1) percent_complete FROM sys.dm_exec_requests WHERE command LIKE 'RESTORE%' ORDER BY start_time DESC;",
                            secondary);
                        var result = await cmd.ExecuteScalarAsync(cts.Token).ConfigureAwait(false);
                        if (result is not null and not DBNull)
                        {
                            var pct = Convert.ToDouble(result);
                            progress.Report(new RestoreProgressEvent(
                                DateTime.UtcNow, $"Restore {pct:0.0}% complete.", RestoreProgressSeverity.Info, pct));
                        }
                    }
                    catch
                    {
                        // Best-effort supplement only - never fail the restore because polling failed.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the restore finishes and the poller is disposed.
            }
        }, cts.Token);

        return new CancelOnDispose(cts);
    }

    private sealed class CancelOnDispose(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static readonly IReadOnlyList<RestoreParameterDescriptor> s_descriptors = new[]
    {
        new RestoreParameterDescriptor("Database", RestoreParameterKind.String, null, RestoreParameterGroup.SourceAndTarget, "Source database name to restore from backups."),
        new RestoreParameterDescriptor("RestoreDatabaseName", RestoreParameterKind.String, null, RestoreParameterGroup.SourceAndTarget, "Name to give the restored database (defaults to the source name)."),
        new RestoreParameterDescriptor("DatabaseOwner", RestoreParameterKind.String, null, RestoreParameterGroup.SourceAndTarget, "Login to set as the restored database's owner."),
        new RestoreParameterDescriptor("ExistingDBAction", RestoreParameterKind.Int, 0, RestoreParameterGroup.SourceAndTarget, "0 = fail if exists (safe), 1 = kill active connections & overwrite, 2 = take offline first, 3 = drop first. Anything but 0 is destructive and requires confirmation."),

        new RestoreParameterDescriptor("BackupPathFull", RestoreParameterKind.Path, null, RestoreParameterGroup.BackupLocations, "Folder containing full backups."),
        new RestoreParameterDescriptor("BackupPathDiff", RestoreParameterKind.Path, null, RestoreParameterGroup.BackupLocations, "Folder containing differential backups (optional)."),
        new RestoreParameterDescriptor("BackupPathLog", RestoreParameterKind.Path, null, RestoreParameterGroup.BackupLocations, "Folder containing transaction log backups (optional)."),
        new RestoreParameterDescriptor("FileNamePrefix", RestoreParameterKind.String, null, RestoreParameterGroup.BackupLocations, "Only consider backup files starting with this prefix."),
        new RestoreParameterDescriptor("FileExtensionBak", RestoreParameterKind.String, null, RestoreParameterGroup.BackupLocations, "Custom file extension for full backups (default assumes .bak; log backups are always .trn)."),
        new RestoreParameterDescriptor("FileExtensionDiff", RestoreParameterKind.String, null, RestoreParameterGroup.BackupLocations, "Custom file extension for differential backups (default assumes .bak)."),
        new RestoreParameterDescriptor("SimpleFolderEnumeration", RestoreParameterKind.Bit, false, RestoreParameterGroup.BackupLocations, "Use simple (non-recursive) folder enumeration."),
        new RestoreParameterDescriptor("SkipBackupsAlreadyInMsdb", RestoreParameterKind.Bit, false, RestoreParameterGroup.BackupLocations, "Skip backup files already recorded in msdb history."),

        new RestoreParameterDescriptor("StopAt", RestoreParameterKind.DateTime, null, RestoreParameterGroup.PointInTimeAndChain, "Point-in-time to stop the restore at (defaults to latest available)."),
        new RestoreParameterDescriptor("OnlyLogsAfter", RestoreParameterKind.DateTime, null, RestoreParameterGroup.PointInTimeAndChain, "Ignore log backups finished before this time."),
        new RestoreParameterDescriptor("RestoreDiff", RestoreParameterKind.Bit, false, RestoreParameterGroup.PointInTimeAndChain, "Apply the latest differential backup, if one exists."),
        new RestoreParameterDescriptor("ContinueLogs", RestoreParameterKind.Bit, false, RestoreParameterGroup.PointInTimeAndChain, "Continue an existing NORECOVERY restore: skip the FULL/DIFF and only apply newer log backups. Leave off for a normal restore - logs are applied whenever a log folder is set."),

        new RestoreParameterDescriptor("MoveFiles", RestoreParameterKind.Bit, false, RestoreParameterGroup.FilePlacement, "Relocate data/log files to different drives on restore."),
        new RestoreParameterDescriptor("MoveDataDrive", RestoreParameterKind.Path, null, RestoreParameterGroup.FilePlacement, "Destination folder for data files when MoveFiles is set."),
        new RestoreParameterDescriptor("MoveLogDrive", RestoreParameterKind.Path, null, RestoreParameterGroup.FilePlacement, "Destination folder for the log file when MoveFiles is set."),
        new RestoreParameterDescriptor("MoveFilestreamDrive", RestoreParameterKind.Path, null, RestoreParameterGroup.FilePlacement, "Destination folder for FILESTREAM data when MoveFiles is set."),
        new RestoreParameterDescriptor("MoveFullTextCatalogDrive", RestoreParameterKind.Path, null, RestoreParameterGroup.FilePlacement, "Destination folder for full-text catalogs when MoveFiles is set."),

        new RestoreParameterDescriptor("RunRecovery", RestoreParameterKind.Bit, true, RestoreParameterGroup.RecoveryBehavior, "Bring the database online (WITH RECOVERY) once all backups are applied. Leave unchecked for log shipping / standby."),
        new RestoreParameterDescriptor("StandbyMode", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Restore WITH STANDBY instead of NORECOVERY, leaving the database readable between log restores."),
        new RestoreParameterDescriptor("StandbyUndoPath", RestoreParameterKind.Path, null, RestoreParameterGroup.RecoveryBehavior, "Undo file path required when StandbyMode is set."),
        new RestoreParameterDescriptor("ForceSimpleRecovery", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Force SIMPLE recovery model after restore, skipping log backups."),
        new RestoreParameterDescriptor("SetTrustworthyON", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Set TRUSTWORTHY ON after restore."),
        new RestoreParameterDescriptor("FixOrphanUsers", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Re-map orphaned database users to server logins after restore."),
        new RestoreParameterDescriptor("KeepCdc", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Preserve Change Data Capture settings through the restore."),
        new RestoreParameterDescriptor("EnableBroker", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Enable Service Broker on the restored database."),

        new RestoreParameterDescriptor("TestRestore", RestoreParameterKind.Bit, false, RestoreParameterGroup.ValidationAndSafety, "Restore under a throwaway name to verify backups are usable, without touching the real database."),
        new RestoreParameterDescriptor("RunCheckDB", RestoreParameterKind.Bit, false, RestoreParameterGroup.ValidationAndSafety, "Run DBCC CHECKDB after the restore completes (can be slow)."),

        new RestoreParameterDescriptor("BufferCount", RestoreParameterKind.Int, null, RestoreParameterGroup.PerformanceTuning, "RESTORE BUFFERCOUNT tuning value."),
        new RestoreParameterDescriptor("MaxTransferSize", RestoreParameterKind.Int, null, RestoreParameterGroup.PerformanceTuning, "RESTORE MAXTRANSFERSIZE tuning value, in bytes."),
        new RestoreParameterDescriptor("BlockSize", RestoreParameterKind.Int, null, RestoreParameterGroup.PerformanceTuning, "RESTORE BLOCKSIZE tuning value, in bytes."),

        new RestoreParameterDescriptor("Execute", RestoreParameterKind.YesNoChar, false, RestoreParameterGroup.ExecutionAndDiagnostics, "N = print the resolved commands only. Y = actually run them. Controlled by the Preview/Run buttons."),
        new RestoreParameterDescriptor("Debug", RestoreParameterKind.Int, 0, RestoreParameterGroup.ExecutionAndDiagnostics, "Verbosity level for diagnostic output (0 = normal)."),
        new RestoreParameterDescriptor("RunStoredProcAfterRestore", RestoreParameterKind.String, null, RestoreParameterGroup.ExecutionAndDiagnostics, "Fully-qualified stored procedure to run automatically after the restore completes."),
    };
}
