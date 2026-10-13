namespace SQLRestoreSOS.Core.BackupChain.Engines;

/// <summary>The CLR/control shape a RestoreParameterDescriptor needs, driving the Advanced tab's data-driven controls.</summary>
public enum RestoreParameterKind
{
    Bit,
    Int,
    String,
    Path,
    DateTime,

    /// <summary>A bit presented in the proc as CHAR(1) 'Y'/'N' (e.g. sp_DatabaseRestore's @Execute).</summary>
    YesNoChar
}
