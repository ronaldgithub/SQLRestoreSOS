namespace SQLRestoreSOS.Core.Connections;

/// <summary>
/// Holds everything needed to open a SQL Server connection. Password lives only
/// in memory for the lifetime of the process - it is never written to settings.json.
/// </summary>
public sealed class ConnectionProfile
{
    public required string ServerName { get; init; }

    public SqlAuthMode AuthMode { get; init; } = SqlAuthMode.Windows;

    public string? UserName { get; init; }

    public string? Password { get; init; }
}
