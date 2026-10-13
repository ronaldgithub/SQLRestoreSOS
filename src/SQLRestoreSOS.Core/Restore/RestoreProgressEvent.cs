namespace SQLRestoreSOS.Core.Restore;

public enum RestoreProgressSeverity
{
    Info,
    Warning,
    Error
}

public sealed record RestoreProgressEvent(
    DateTime TimestampUtc,
    string Message,
    RestoreProgressSeverity Severity,
    double? PercentComplete = null);
