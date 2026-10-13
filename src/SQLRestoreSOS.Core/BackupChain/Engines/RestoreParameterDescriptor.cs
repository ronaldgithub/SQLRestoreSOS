namespace SQLRestoreSOS.Core.BackupChain.Engines;

/// <summary>Describes one parameter of an engine's underlying stored procedure, for data-driven Advanced tab UI generation.</summary>
public sealed record RestoreParameterDescriptor(
    string Name,
    RestoreParameterKind Kind,
    object? DefaultValue,
    RestoreParameterGroup Group,
    string HelpText);
