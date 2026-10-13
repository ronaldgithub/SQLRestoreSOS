using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SQLRestoreSOS.App.ViewModels;
using SQLRestoreSOS.Core.Connections;

namespace SQLRestoreSOS.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireViewModel();
    }

    private void WireViewModel()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.ShowConnectDialog = async connectVm =>
        {
            var dialog = new ConnectDialog(connectVm);
            return await dialog.ShowDialog<ConnectionProfile?>(this);
        };

        vm.ShowAboutDialog = async aboutVm =>
        {
            var dialog = new AboutDialog(aboutVm);
            await dialog.ShowDialog(this);
        };

        vm.ShowConfirmationDialog = async confirmVm =>
        {
            var dialog = new RestoreConfirmationDialog(confirmVm);
            return await dialog.ShowDialog<bool>(this);
        };

        vm.ShowNewLogFoundDialog = async logVm =>
        {
            var dialog = new NewLogFoundDialog(logVm);
            return await dialog.ShowDialog<bool>(this);
        };

        vm.ShowFileListDialog = async fileVm =>
        {
            var dialog = new FileListPreviewDialog(fileVm);
            await dialog.ShowDialog(this);
        };

        vm.ShowError = async message =>
        {
            var dialog = new Window
            {
                Title = "SQLRestoreSOS",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBlock
                {
                    Text = message,
                    Margin = new Avalonia.Thickness(20),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            await dialog.ShowDialog(this);
        };
    }
}
