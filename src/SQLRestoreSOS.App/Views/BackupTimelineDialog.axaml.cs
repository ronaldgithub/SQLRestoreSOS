using Avalonia.Controls;
using SQLRestoreSOS.App.ViewModels;

namespace SQLRestoreSOS.App.Views;

public partial class BackupTimelineDialog : Window
{
    public BackupTimelineDialog()
    {
        InitializeComponent();
    }

    public BackupTimelineDialog(BackupTimelineDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
    }
}
