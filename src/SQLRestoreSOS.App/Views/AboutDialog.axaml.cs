using Avalonia.Controls;
using SQLRestoreSOS.App.ViewModels;

namespace SQLRestoreSOS.App.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    public AboutDialog(AboutDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
    }
}
