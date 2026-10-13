using Avalonia.Controls;
using SQLRestoreSOS.App.ViewModels;

namespace SQLRestoreSOS.App.Views;

public partial class NewLogFoundDialog : Window
{
    public NewLogFoundDialog()
    {
        InitializeComponent();
    }

    public NewLogFoundDialog(NewLogFoundDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += apply => Close(apply);
    }
}
