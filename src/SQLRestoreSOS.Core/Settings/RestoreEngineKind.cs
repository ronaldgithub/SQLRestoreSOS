namespace SQLRestoreSOS.Core.Settings;

public enum RestoreEngineKind
{
    /// <summary>sp_DatabaseRestore (Brent Ozar Unlimited / FirstResponderKit.org, MIT) - bundled, primary engine.</summary>
    DatabaseRestore,

    /// <summary>sp_RestoreGene (Paul Brewer / restoregene.com) - optional, user-installed, not bundled.</summary>
    RestoreGene
}
