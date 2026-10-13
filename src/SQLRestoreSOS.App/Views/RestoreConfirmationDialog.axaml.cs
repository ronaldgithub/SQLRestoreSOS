using Avalonia.Controls;
using SQLRestoreSOS.App.ViewModels;

namespace SQLRestoreSOS.App.Views;

public partial class RestoreConfirmationDialog : Window
{
    public RestoreConfirmationDialog()
    {
        InitializeComponent();
    }

    public RestoreConfirmationDialog(RestoreConfirmationDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += confirmed => Close(confirmed);
    }
}
