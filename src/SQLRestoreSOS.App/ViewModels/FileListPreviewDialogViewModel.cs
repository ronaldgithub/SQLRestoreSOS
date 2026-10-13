using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SQLRestoreSOS.Core.BackupChain;

namespace SQLRestoreSOS.App.ViewModels;

/// <summary>Shows the result of RESTORE FILELISTONLY for one backup file - the "preview files" option.</summary>
public partial class FileListPreviewDialogViewModel : ViewModelBase
{
    public required string BackupFilePath { get; init; }

    public ObservableCollection<BackupFileListEntry> Files { get; } = new();

    public event Action? RequestClose;

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();
}
