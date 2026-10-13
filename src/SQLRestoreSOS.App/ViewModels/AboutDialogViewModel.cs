using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace SQLRestoreSOS.App.ViewModels;

public partial class AboutDialogViewModel : ViewModelBase
{
    public string AppName => "SQLRestoreSOS";

    public string Version
    {
        get
        {
            var v = typeof(AboutDialogViewModel).Assembly.GetName().Version;
            return v is null ? string.Empty : $"Version {v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public string Description =>
        "Point-in-time SQL Server restore for Junior DBAs - walk the backup chain and run a " +
        "disaster recovery restore (SOS - 911 / 112) by clicking, not by memorizing parameters.";

    public string LicenseSummary => "MIT License (c) Ronald de Groot";

    public string DatabaseRestoreAttribution =>
        "Primary restore engine 'sp_DatabaseRestore' (C) Brent Ozar Unlimited / FirstResponderKit.org - MIT License, bundled.";

    public string RestoreGeneAttribution =>
        "Optional restore engine 'sp_RestoreGene' (C) Paul Brewer / restoregene.com - a separate product, " +
        "not bundled or relicensed by this app.";

    public string Email => "ronald.de.groot@opendata.nl";

    public string WebsiteCom => "www.dbaronald.com";

    public string WebsiteNl => "dbaronald.nl";

    public event Action? RequestClose;

    [RelayCommand]
    private void OpenEmail() => Open("mailto:ronald.de.groot@opendata.nl");

    [RelayCommand]
    private void OpenWebsiteCom() => Open("https://www.dbaronald.com");

    [RelayCommand]
    private void OpenWebsiteNl() => Open("https://dbaronald.nl");

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();
}
