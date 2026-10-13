using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLRestoreSOS.Core.Connections;

namespace SQLRestoreSOS.App.ViewModels;

public partial class ConnectDialogViewModel : ViewModelBase
{
    private readonly SqlConnectionService _connectionService = new();

    [ObservableProperty]
    private string _serverName = "localhost";

    [ObservableProperty]
    private bool _isWindowsAuth = true;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string? _testResultMessage;

    [ObservableProperty]
    private bool? _testSucceeded;

    public ObservableCollection<string> RecentServers { get; } = new();

    public event Action<ConnectionProfile?>? RequestClose;

    public ConnectDialogViewModel()
    {
    }

    public ConnectDialogViewModel(IEnumerable<string> recentServers)
    {
        foreach (var s in recentServers)
        {
            RecentServers.Add(s);
        }

        // ServerName always defaults to "localhost" (see field initializer above) regardless of
        // recent-server history - recent servers are offered via the AutoCompleteBox dropdown
        // instead, so picking a previous server is a click away without the default being
        // unpredictable based on what was last typed.
    }

    partial void OnIsWindowsAuthChanged(bool value)
    {
        TestSucceeded = null;
        TestResultMessage = null;
    }

    private ConnectionProfile BuildProfile() => new()
    {
        ServerName = ServerName.Trim(),
        AuthMode = IsWindowsAuth ? SqlAuthMode.Windows : SqlAuthMode.SqlLogin,
        UserName = IsWindowsAuth ? null : UserName,
        Password = IsWindowsAuth ? null : Password
    };

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
        {
            TestResultMessage = "Enter a server name first.";
            TestSucceeded = false;
            return;
        }

        IsTesting = true;
        TestResultMessage = null;
        try
        {
            var ok = await _connectionService.TestConnectionAsync(BuildProfile());
            TestSucceeded = ok;
            TestResultMessage = ok
                ? "Connection succeeded."
                : "Could not connect. Check the server name, authentication, and that SQL Server allows remote connections.";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
        {
            TestResultMessage = "Enter a server name first.";
            TestSucceeded = false;
            return;
        }

        RequestClose?.Invoke(BuildProfile());
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(null);
}
