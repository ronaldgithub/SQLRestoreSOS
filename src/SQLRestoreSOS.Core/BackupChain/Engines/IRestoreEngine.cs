using Microsoft.Data.SqlClient;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.Core.BackupChain.Engines;

/// <summary>Engine-agnostic contract both sp_DatabaseRestore and sp_RestoreGene adapters implement.</summary>
public interface IRestoreEngine
{
    string Name { get; }

    Task<BackupChainTimeline> DiscoverBackupChainAsync(
        SqlConnection conn,
        BackupChainQuery query,
        CancellationToken ct = default);

    /// <summary>Safe, Basic-tab-appropriate defaults derived from an engine-agnostic request.</summary>
    RestoreParameterSet GetDefaultParameters(RestoreRequest request);

    /// <summary>Drives the Advanced tab's data-driven parameter list.</summary>
    IReadOnlyList<RestoreParameterDescriptor> DescribeParameters();

    /// <summary>Pure, offline, unit-testable: the copy/paste-ready EXEC call text for the current parameters.</summary>
    string BuildScript(RestoreRequest request, RestoreParameterSet parameters);

    /// <summary>
    /// The "-WhatIf" equivalent: connects to the server and resolves the actual low-level
    /// RESTORE DATABASE/RESTORE LOG statements the engine would run for these parameters, without
    /// making any changes (forces a dry run regardless of the parameters' own Execute flag). This
    /// is different from BuildScript, which only ever returns the offline wrapper EXEC call text -
    /// the backup files on disk/in msdb aren't enumerated until the proc actually runs server-side.
    /// </summary>
    Task<string> ResolveActualStatementsAsync(
        SqlConnection conn,
        RestoreRequest request,
        RestoreParameterSet parameters,
        CancellationToken ct = default);

    /// <summary>
    /// Runs the EXEC call built by BuildScript against the server. If parameters' Execute flag
    /// is false this resolves/previews only (no data changes); if true it performs the real restore.
    /// Streams progress (RAISERROR/InfoMessage text, and percent-complete where available) via <paramref name="progress"/>.
    /// </summary>
    Task ExecuteAsync(
        SqlConnection conn,
        RestoreRequest request,
        RestoreParameterSet parameters,
        IProgress<RestoreProgressEvent>? progress,
        CancellationToken ct = default);
}
