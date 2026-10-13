using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SQLRestoreSOS.App.ViewModels;

/// <summary>
/// Gate shown before any destructive restore (ExistingDatabaseAction != DoNothing, which may kill
/// active connections and/or drop the target database). The DBA must explicitly acknowledge the
/// risk before Confirm becomes clickable.
/// </summary>
public partial class RestoreConfirmationDialogViewModel : ViewModelBase
{
    public required string TargetDatabase { get; init; }

    public required string WarningText { get; init; }

    public required string SummaryText { get; init; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isAcknowledged;

    public event Action<bool>? RequestClose;

    [RelayCommand(CanExecute = nameof(IsAcknowledged))]
    private void Confirm() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
