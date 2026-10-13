using Microsoft.Data.SqlClient;

namespace SQLRestoreSOS.Core.Connections;

public sealed class SqlConnectionService
{
    public string BuildConnectionString(ConnectionProfile profile, string? initialCatalog = null)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.ServerName,
            InitialCatalog = initialCatalog ?? "master",
            ConnectTimeout = 5,
            TrustServerCertificate = true
        };

        // Indexer avoids depending on the Encrypt property's exact CLR type, which has
        // changed across Microsoft.Data.SqlClient versions (bool -> SqlConnectionEncryptOption).
        builder["Encrypt"] = "Optional";

        if (profile.AuthMode == SqlAuthMode.Windows)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = profile.UserName ?? string.Empty;
            builder.Password = profile.Password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    public async Task<bool> TestConnectionAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString(profile));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    public async Task<SqlConnection> OpenConnectionAsync(
        ConnectionProfile profile,
        string? initialCatalog = null,
        CancellationToken ct = default)
    {
        var conn = new SqlConnection(BuildConnectionString(profile, initialCatalog));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }
}
