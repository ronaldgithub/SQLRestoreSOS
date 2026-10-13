using Microsoft.Data.SqlClient;

namespace SQLRestoreSOS.Core.Databases;

public sealed class DatabaseCatalogService
{
    public async Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(
        SqlConnection conn,
        bool includeSystemDatabases = false,
        CancellationToken ct = default)
    {
        var sql = includeSystemDatabases
            ? "SELECT name, state_desc, recovery_model_desc FROM sys.databases ORDER BY name;"
            : "SELECT name, state_desc, recovery_model_desc FROM sys.databases WHERE database_id > 4 ORDER BY name;";

        var results = new List<DatabaseInfo>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new DatabaseInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return results;
    }
}
