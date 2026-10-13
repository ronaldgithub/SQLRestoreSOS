using Microsoft.Data.SqlClient;

namespace SQLRestoreSOS.Core.BackupChain;

/// <summary>
/// Wraps RESTORE FILELISTONLY - the "preview files inside a backup" option. Both bundled
/// engines use this same mechanism internally (sp_DatabaseRestore populates #FileListParameters
/// from it); exposing it standalone lets a DBA sanity-check a backup and read off the logical
/// file names before relying on them for the Move-Files drive-mapping fields.
/// </summary>
public sealed class BackupFileInspector
{
    public async Task<IReadOnlyList<BackupFileListEntry>> GetFileListAsync(
        SqlConnection conn,
        string backupFilePath,
        CancellationToken ct = default)
    {
        const string sql = "RESTORE FILELISTONLY FROM DISK = @path;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@path", backupFilePath);

        var results = new List<BackupFileListEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var logicalOrd = reader.GetOrdinal("LogicalName");
        var physicalOrd = reader.GetOrdinal("PhysicalName");
        var typeOrd = reader.GetOrdinal("Type");
        var fileGroupOrd = reader.GetOrdinal("FileGroupName");
        var sizeOrd = reader.GetOrdinal("Size");
        var maxSizeOrd = reader.GetOrdinal("MaxSize");

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapFileListRow(
                logicalName: reader.GetString(logicalOrd),
                physicalName: reader.GetString(physicalOrd),
                type: reader.IsDBNull(typeOrd) ? "D" : reader.GetString(typeOrd),
                fileGroupName: reader.IsDBNull(fileGroupOrd) ? null : reader.GetString(fileGroupOrd),
                size: Convert.ToInt64(reader.GetValue(sizeOrd)),
                maxSize: Convert.ToInt64(reader.GetValue(maxSizeOrd))));
        }

        return results;
    }

    /// <summary>Pure mapping logic, kept separate from ADO.NET so it is unit-testable with fake row data.</summary>
    internal static BackupFileListEntry MapFileListRow(
        string logicalName,
        string physicalName,
        string type,
        string? fileGroupName,
        long size,
        long maxSize)
    {
        var typeChar = string.IsNullOrEmpty(type) ? 'D' : char.ToUpperInvariant(type[0]);
        return new BackupFileListEntry(logicalName, physicalName, typeChar, fileGroupName, size, maxSize);
    }
}
