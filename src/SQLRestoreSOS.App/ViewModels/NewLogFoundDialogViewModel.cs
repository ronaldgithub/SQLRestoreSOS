using CommunityToolkit.Mvvm.Input;

namespace SQLRestoreSOS.App.ViewModels;

/// <summary>Log-shipping mode: shown when LogShippingSession finds a new log backup on disk.</summary>
public partial class NewLogFoundDialogViewModel : ViewModelBase
{
    public required string DatabaseName { get; init; }

    public required string LogFilePath { get; init; }

    public required DateTime BackupFinishDateUtc { get; init; }

    public event Action<bool>? RequestClose;

    [RelayCommand]
    private void Apply() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Skip() => RequestClose?.Invoke(false);
}
