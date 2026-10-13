using System.Text;
using Microsoft.Data.SqlClient;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.Core.BackupChain.Engines;

/// <summary>
/// Optional/secondary engine: adapts sp_RestoreGene (Paul Brewer / restoregene.com). Not bundled
/// with this app (see README - no clear redistribution license in the script), so this engine is
/// only usable when the DBA has already installed it themselves on the connected server. It is a
/// pure script *generator* - it never executes anything itself, so ExecuteAsync here means
/// "call the proc to get the script text, then run that script ourselves".
/// </summary>
public sealed class RestoreGeneEngine : IRestoreEngine
{
    public string Name => "sp_RestoreGene";

    /// <summary>True if sp_RestoreGene is actually installed on the connected server.</summary>
    public static async Task<bool> IsInstalledAsync(SqlConnection conn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT OBJECT_ID('master.dbo.sp_RestoreGene');", conn);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null and not DBNull;
    }

    public RestoreParameterSet GetDefaultParameters(RestoreRequest request)
    {
        var p = new RestoreParameterSet();

        p.Set("Database", request.SourceDatabase);
        if (!string.IsNullOrWhiteSpace(request.TargetDatabase) &&
            !string.Equals(request.TargetDatabase, request.SourceDatabase, StringComparison.OrdinalIgnoreCase))
        {
            p.Set("TargetDatabase", request.TargetDatabase);
        }

        if (request.PointInTimeUtc is { } stopAt)
        {
            p.Set("StopAt", stopAt);
        }

        // Log-shipping mode always stays out of recovery, regardless of point-in-time.
        p.Set("WithRecovery", !request.KeepInNoRecoveryForLogShipping);
        p.Set("WithReplace", request.ExistingDatabaseAction != ExistingDatabaseAction.DoNothing);
        p.Set("WithCHECKDB", request.RunCheckDbAfterRestore);

        // Always request script-only output - ExecuteAsync depends on getting script text back.
        p.Set("RestoreScriptOnly", true);

        if (request.MoveFiles)
        {
            p.Set("SuppressWithMove", false);
            if (!string.IsNullOrWhiteSpace(request.MoveDataDrive))
            {
                p.Set("WithMoveDataFiles", request.MoveDataDrive);
            }

            if (!string.IsNullOrWhiteSpace(request.MoveLogDrive))
            {
                p.Set("WithMoveLogFile", request.MoveLogDrive);
            }
        }

        return p;
    }

    public IReadOnlyList<RestoreParameterDescriptor> DescribeParameters() => s_descriptors;

    public string BuildScript(RestoreRequest request, RestoreParameterSet parameters)
    {
        var ordered = new (string Name, RestoreParameterKind Kind, object? Value)[]
        {
            ("Database", RestoreParameterKind.String, parameters.Get("Database")),
            ("TargetDatabase", RestoreParameterKind.String, parameters.Get("TargetDatabase")),
            ("WithMoveDataFiles", RestoreParameterKind.Path, parameters.Get("WithMoveDataFiles")),
            ("WithMoveLogFile", RestoreParameterKind.Path, parameters.Get("WithMoveLogFile")),
            ("WithMoveFileStreamFile", RestoreParameterKind.Path, parameters.Get("WithMoveFileStreamFile")),
            ("FromFileFullUNC", RestoreParameterKind.Path, parameters.Get("FromFileFullUNC")),
            ("FromFileDiffUNC", RestoreParameterKind.Path, parameters.Get("FromFileDiffUNC")),
            ("FromFileLogUNC", RestoreParameterKind.Path, parameters.Get("FromFileLogUNC")),
            ("StopAt", RestoreParameterKind.DateTime, parameters.Get("StopAt")),
            ("StandbyMode", RestoreParameterKind.Bit, parameters.Get("StandbyMode")),
            ("IncludeSystemDBs", RestoreParameterKind.Bit, parameters.Get("IncludeSystemDBs")),
            ("WithRecovery", RestoreParameterKind.Bit, parameters.Get("WithRecovery")),
            ("WithCHECKDB", RestoreParameterKind.Bit, parameters.Get("WithCHECKDB")),
            ("WithReplace", RestoreParameterKind.Bit, parameters.Get("WithReplace")),
            ("BlobCredential", RestoreParameterKind.String, parameters.Get("BlobCredential")),
            ("RestoreScriptReplaceThis", RestoreParameterKind.String, parameters.Get("RestoreScriptReplaceThis")),
            ("RestoreScriptWithThis", RestoreParameterKind.String, parameters.Get("RestoreScriptWithThis")),
            ("SuppressWithMove", RestoreParameterKind.Bit, parameters.Get("SuppressWithMove")),
            ("PivotWithMove", RestoreParameterKind.Bit, parameters.Get("PivotWithMove")),
            ("RestoreScriptOnly", RestoreParameterKind.Bit, parameters.Get("RestoreScriptOnly")),
            ("DropDatabaseAfterRestore", RestoreParameterKind.Bit, parameters.Get("DropDatabaseAfterRestore")),
            ("SetSingleUser", RestoreParameterKind.Bit, parameters.Get("SetSingleUser")),
            ("ExcludeDiffAndLogBackups", RestoreParameterKind.Int, parameters.Get("ExcludeDiffAndLogBackups")),
            ("IncludeDeviceType7", RestoreParameterKind.Bit, parameters.Get("IncludeDeviceType7")),
            ("IncludeDeviceType102", RestoreParameterKind.Bit, parameters.Get("IncludeDeviceType102")),
            ("IncludeDeviceType2", RestoreParameterKind.Bit, parameters.Get("IncludeDeviceType2")),
            ("IncludeDeviceType9", RestoreParameterKind.Bit, parameters.Get("IncludeDeviceType9")),
        };

        // sp_RestoreGene's installer creates it in [master] (USE [master] precedes the CREATE PROC),
        // so it's addressed via master.dbo rather than msdb.dbo.
        return RestoreScriptBuilder.BuildExecCall(
            "master.dbo.sp_RestoreGene",
            ordered,
            headerComment: "sp_RestoreGene (C) Paul Brewer - https://restoregene.com - not bundled/relicensed by SQLRestoreSOS, install separately");
    }

    public Task<string> ResolveActualStatementsAsync(
        SqlConnection conn,
        RestoreRequest request,
        RestoreParameterSet parameters,
        CancellationToken ct = default) =>
        // This engine never executes anything itself - generating the script IS the dry run, so
        // the same code path ExecuteAsync uses to fetch it also answers "-WhatIf" here.
        FetchGeneratedScriptAsync(conn, request, parameters, ct);

    public async Task ExecuteAsync(
        SqlConnection conn,
        RestoreRequest request,
        RestoreParameterSet parameters,
        IProgress<RestoreProgressEvent>? progress,
        CancellationToken ct = default)
    {
        var generatedScript = await FetchGeneratedScriptAsync(conn, request, parameters, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(generatedScript))
        {
            progress?.Report(new RestoreProgressEvent(
                DateTime.UtcNow, "sp_RestoreGene returned no restore commands for this database/point in time.", RestoreProgressSeverity.Warning));
            return;
        }

        if (!request.ExecuteImmediately)
        {
            progress?.Report(new RestoreProgressEvent(DateTime.UtcNow, generatedScript, RestoreProgressSeverity.Info));
            return;
        }

        SqlInfoMessageEventHandler handler = (_, e) =>
        {
            foreach (SqlError err in e.Errors)
            {
                var severity = err.Class >= 11 ? RestoreProgressSeverity.Error : RestoreProgressSeverity.Info;
                progress?.Report(new RestoreProgressEvent(DateTime.UtcNow, err.Message, severity));
            }
        };

        conn.InfoMessage += handler;
        try
        {
            foreach (var batch in SplitOnGo(generatedScript))
            {
                if (string.IsNullOrWhiteSpace(batch))
                {
                    continue;
                }

                await using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 0 };
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            progress?.Report(new RestoreProgressEvent(DateTime.UtcNow, "Restore completed.", RestoreProgressSeverity.Info));
        }
        finally
        {
            conn.InfoMessage -= handler;
        }
    }

    public async Task<BackupChainTimeline> DiscoverBackupChainAsync(
        SqlConnection conn,
        BackupChainQuery query,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT bs.type, bs.first_lsn, bs.last_lsn, bs.backup_start_date, bs.backup_finish_date, mf.physical_device_name
            FROM msdb.dbo.backupset bs
            JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id = bs.media_set_id
            WHERE bs.database_name = @db
            ORDER BY bs.backup_finish_date;
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", query.DatabaseName);

        var entries = new List<BackupFileEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var typeCode = reader.GetString(0);
            BackupType? mapped = typeCode switch
            {
                "D" => BackupType.Full,
                "I" => BackupType.Differential,
                "L" => BackupType.Log,
                _ => null
            };

            if (mapped is null)
            {
                continue;
            }

            entries.Add(new BackupFileEntry(
                reader.GetString(5),
                mapped.Value,
                reader.GetDateTime(3),
                reader.GetDateTime(4),
                reader.GetDecimal(1),
                reader.GetDecimal(2)));
        }

        return new BackupChainTimeline(entries);
    }

    private async Task<string> FetchGeneratedScriptAsync(
        SqlConnection conn, RestoreRequest request, RestoreParameterSet parameters, CancellationToken ct)
    {
        // Force RestoreScriptOnly=1 for the actual call regardless of the Advanced-tab value, so
        // we reliably get script text back rather than the proc's full diagnostic result set.
        var effectiveParameters = parameters.Clone();
        effectiveParameters.Set("RestoreScriptOnly", true);

        var script = BuildScript(request, effectiveParameters);

        await using var cmd = new SqlCommand(script, conn) { CommandTimeout = 0 };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // Defensive: find whichever string column(s) in this row look like restore command
            // text, rather than depending on an exact column name/position that may vary by version.
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i))
                {
                    continue;
                }

                if (reader.GetFieldType(i) != typeof(string))
                {
                    continue;
                }

                var value = reader.GetString(i);
                if (value.Contains("RESTORE ", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(value.TrimEnd());
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<string> SplitOnGo(string script) =>
        script.Split(["\r\nGO\r\n", "\nGO\n", "\r\nGO", "\nGO"], StringSplitOptions.None);

    private static readonly IReadOnlyList<RestoreParameterDescriptor> s_descriptors = new[]
    {
        new RestoreParameterDescriptor("Database", RestoreParameterKind.String, null, RestoreParameterGroup.SourceAndTarget, "Database name (or comma-separated list) to restore. NULL restores all user databases."),
        new RestoreParameterDescriptor("TargetDatabase", RestoreParameterKind.String, null, RestoreParameterGroup.SourceAndTarget, "Override the restored database name (single-database restores only)."),
        new RestoreParameterDescriptor("WithReplace", RestoreParameterKind.Bit, false, RestoreParameterGroup.SourceAndTarget, "Overwrite the target database if it already exists. Destructive - requires confirmation."),

        new RestoreParameterDescriptor("FromFileFullUNC", RestoreParameterKind.Path, null, RestoreParameterGroup.BackupLocations, "Override the full backup file location recorded in msdb (if it has moved)."),
        new RestoreParameterDescriptor("FromFileDiffUNC", RestoreParameterKind.Path, null, RestoreParameterGroup.BackupLocations, "Override the differential backup file location recorded in msdb."),
        new RestoreParameterDescriptor("FromFileLogUNC", RestoreParameterKind.Path, null, RestoreParameterGroup.BackupLocations, "Override the log backup file location recorded in msdb."),
        new RestoreParameterDescriptor("ExcludeDiffAndLogBackups", RestoreParameterKind.Int, 0, RestoreParameterGroup.BackupLocations, "0 = full+diff+log, 1 = full only, 2 = full+diff, 3 = full+log, 4 = diff+log only."),
        new RestoreParameterDescriptor("IncludeDeviceType7", RestoreParameterKind.Bit, false, RestoreParameterGroup.BackupLocations, "Include virtual-device (type 7) backups, e.g. AWS EC2."),
        new RestoreParameterDescriptor("IncludeDeviceType102", RestoreParameterKind.Bit, true, RestoreParameterGroup.BackupLocations, "Include disk (type 102) backups."),
        new RestoreParameterDescriptor("IncludeDeviceType2", RestoreParameterKind.Bit, true, RestoreParameterGroup.BackupLocations, "Include tape (type 2) backups."),
        new RestoreParameterDescriptor("IncludeDeviceType9", RestoreParameterKind.Bit, true, RestoreParameterGroup.BackupLocations, "Include virtual backup device (type 9) backups."),

        new RestoreParameterDescriptor("StopAt", RestoreParameterKind.DateTime, null, RestoreParameterGroup.PointInTimeAndChain, "Point-in-time to stop the restore at (defaults to now)."),

        new RestoreParameterDescriptor("WithMoveDataFiles", RestoreParameterKind.Path, null, RestoreParameterGroup.FilePlacement, "Destination folder for data files."),
        new RestoreParameterDescriptor("WithMoveLogFile", RestoreParameterKind.Path, null, RestoreParameterGroup.FilePlacement, "Destination folder for the log file."),
        new RestoreParameterDescriptor("WithMoveFileStreamFile", RestoreParameterKind.Path, null, RestoreParameterGroup.FilePlacement, "Destination folder for FILESTREAM data."),
        new RestoreParameterDescriptor("SuppressWithMove", RestoreParameterKind.Bit, true, RestoreParameterGroup.FilePlacement, "Exclude WITH MOVE clauses from the generated script."),
        new RestoreParameterDescriptor("PivotWithMove", RestoreParameterKind.Bit, false, RestoreParameterGroup.FilePlacement, "Pivot secondary WITH MOVE files onto their own result row (not compatible with direct execution)."),

        new RestoreParameterDescriptor("WithRecovery", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Recover the database after restore. Leave unchecked for log shipping / standby."),
        new RestoreParameterDescriptor("StandbyMode", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Leave the database in STANDBY (readable) mode instead of NORECOVERY."),
        new RestoreParameterDescriptor("SetSingleUser", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Add ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE before restoring."),
        new RestoreParameterDescriptor("IncludeSystemDBs", RestoreParameterKind.Bit, false, RestoreParameterGroup.RecoveryBehavior, "Include master/model/msdb when restoring 'all databases'."),

        new RestoreParameterDescriptor("WithCHECKDB", RestoreParameterKind.Bit, false, RestoreParameterGroup.ValidationAndSafety, "Include a DBCC CHECKDB step after recovery."),
        new RestoreParameterDescriptor("DropDatabaseAfterRestore", RestoreParameterKind.Bit, false, RestoreParameterGroup.ValidationAndSafety, "Drop the database again after restore - useful with CHECKDB for backup verification only."),

        new RestoreParameterDescriptor("BlobCredential", RestoreParameterKind.String, null, RestoreParameterGroup.ExecutionAndDiagnostics, "SQL credential name for Azure Blob Storage backups."),
        new RestoreParameterDescriptor("RestoreScriptReplaceThis", RestoreParameterKind.String, null, RestoreParameterGroup.ExecutionAndDiagnostics, "Find this text in the generated script..."),
        new RestoreParameterDescriptor("RestoreScriptWithThis", RestoreParameterKind.String, null, RestoreParameterGroup.ExecutionAndDiagnostics, "...and replace it with this text."),
        new RestoreParameterDescriptor("RestoreScriptOnly", RestoreParameterKind.Bit, true, RestoreParameterGroup.ExecutionAndDiagnostics, "Return just the RESTORE commands (always forced on by this app)."),
    };
}
