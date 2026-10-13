using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using SQLRestoreSOS.App.ViewModels;

namespace SQLRestoreSOS.App.Views;

public partial class SqlPreviewPane : UserControl
{
    public SqlPreviewPane()
    {
        InitializeComponent();

        this.FindControl<Button>("CopyButton")!.Click += async (_, _) => await CopyAsync(vm => vm.SqlPreviewText);
        this.FindControl<Button>("CopyResolvedButton")!.Click += async (_, _) => await CopyAsync(vm => vm.ResolvedStatementsText);
        this.FindControl<Button>("DryRunButton")!.Click += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm || !vm.ResolveActualStatementsCommand.CanExecute(null))
            {
                return;
            }

            await vm.ResolveActualStatementsCommand.ExecuteAsync(null);

            // A fresh dry-run result always starts with the FULL restore step - if the box was
            // scrolled down from a previous (longer) result, Avalonia keeps that scroll position
            // on a Text binding update, which can hide the new FULL statement out of view. Moving
            // the caret to 0 scrolls the viewport back to the top.
            this.FindControl<TextBox>("ResolvedStatementsTextBox")!.CaretIndex = 0;
        };
    }

    private async System.Threading.Tasks.Task CopyAsync(System.Func<MainWindowViewModel, string> selectText)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;
        if (clipboard is not null && DataContext is MainWindowViewModel vm)
        {
            await clipboard.SetTextAsync(selectText(vm));
        }
    }
}
