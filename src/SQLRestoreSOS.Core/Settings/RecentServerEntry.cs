using SQLRestoreSOS.Core.Connections;

namespace SQLRestoreSOS.Core.Settings;

/// <summary>A remembered server name only - never a password.</summary>
public sealed class RecentServerEntry
{
    public required string ServerName { get; set; }

    public DateTime LastUsedUtc { get; set; }

    public SqlAuthMode LastAuthMode { get; set; } = SqlAuthMode.Windows;
}
