using Avalonia.Controls;
using SQLRestoreSOS.App.ViewModels;

namespace SQLRestoreSOS.App.Views;

public partial class FileListPreviewDialog : Window
{
    public FileListPreviewDialog()
    {
        InitializeComponent();
    }

    public FileListPreviewDialog(FileListPreviewDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
    }
}
