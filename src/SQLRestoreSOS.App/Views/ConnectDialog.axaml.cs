using Avalonia.Controls;
using SQLRestoreSOS.App.ViewModels;
using SQLRestoreSOS.Core.Connections;

namespace SQLRestoreSOS.App.Views;

public partial class ConnectDialog : Window
{
    public ConnectDialog()
    {
        InitializeComponent();
    }

    public ConnectDialog(ConnectDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += profile => Close(profile);
    }
}
