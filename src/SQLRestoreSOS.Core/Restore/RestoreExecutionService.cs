using System.Text.Json;
using Microsoft.Data.SqlClient;
using SQLRestoreSOS.Core.BackupChain.Engines;

namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// Thin wrapper around IRestoreEngine.ExecuteAsync that additionally appends every progress
/// line to logs\audit.jsonl (timestamp, server, database, message, severity) for post-incident
/// review, and relays progress to the caller unchanged.
/// </summary>
public sealed class RestoreExecutionService
{
    private readonly string _auditLogPath;

    public RestoreExecutionService(string? auditLogPath = null)
    {
        _auditLogPath = auditLogPath ?? Path.Combine(AppContext.BaseDirectory, "logs", "audit.jsonl");
    }

    public async Task ExecuteAsync(
        IRestoreEngine engine,
        SqlConnection conn,
        string serverName,
        RestoreRequest request,
        RestoreParameterSet parameters,
        IProgress<RestoreProgressEvent>? progress,
        CancellationToken ct = default)
    {
        var auditingProgress = new Progress<RestoreProgressEvent>(e =>
        {
            AppendAuditLine(serverName, request.SourceDatabase, e);
            progress?.Report(e);
        });

        await engine.ExecuteAsync(conn, request, parameters, auditingProgress, ct).ConfigureAwait(false);
    }

    private void AppendAuditLine(string serverName, string database, RestoreProgressEvent e)
    {
        try
        {
            var directory = Path.GetDirectoryName(_auditLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = JsonSerializer.Serialize(new
            {
                e.TimestampUtc,
                Server = serverName,
                Database = database,
                e.Message,
                Severity = e.Severity.ToString(),
                e.PercentComplete
            });

            File.AppendAllText(_auditLogPath, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Audit logging is best-effort - never let a logging failure abort a restore.
        }
    }
}
