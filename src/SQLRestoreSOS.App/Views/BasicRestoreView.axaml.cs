using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SQLRestoreSOS.App.ViewModels;

namespace SQLRestoreSOS.App.Views;

public partial class BasicRestoreView : UserControl
{
    public BasicRestoreView()
    {
        InitializeComponent();

        this.FindControl<Button>("BrowseRootButton")!.Click += async (_, _) => await BrowseAsync(p => ViewModel.BackupRootPath = p);
        this.FindControl<Button>("BrowseFullButton")!.Click += async (_, _) => await BrowseAsync(p => ViewModel.BackupPathFull = p);
        this.FindControl<Button>("BrowseDiffButton")!.Click += async (_, _) => await BrowseAsync(p => ViewModel.BackupPathDiff = p);
        this.FindControl<Button>("BrowseLogButton")!.Click += async (_, _) => await BrowseAsync(p => ViewModel.BackupPathLog = p);
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private async System.Threading.Tasks.Task BrowseAsync(System.Action<string> onPicked)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select backup folder",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            onPicked(path);
        }
    }
}
