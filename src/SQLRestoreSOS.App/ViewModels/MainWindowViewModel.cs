using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using SQLRestoreSOS.Core.BackupChain;
using SQLRestoreSOS.Core.BackupChain.Engines;
using SQLRestoreSOS.Core.Connections;
using SQLRestoreSOS.Core.Databases;
using SQLRestoreSOS.Core.Restore;
using SQLRestoreSOS.Core.Settings;

namespace SQLRestoreSOS.App.ViewModels;

public sealed record ExistingDbActionOption(ExistingDatabaseAction Value, string Label);

/// <summary>
/// Owns the whole connected-session state: connection, database/backup-chain discovery, the shared
/// RestoreRequest/RestoreParameterSet that both the Basic and Advanced tabs read/write, SQL preview
/// generation, restore execution with live progress, and the log-shipping workflow.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SqlConnectionService _connectionService = new();
    private readonly DatabaseCatalogService _databaseCatalogService = new();
    private readonly BackupFileInspector _fileInspector = new();
    private readonly SettingsService _settingsService = new();
    private readonly RestoreExecutionService _executionService = new();

    private ConnectionProfile? _connectionProfile;
    private SqlConnection? _activeConnection;
    private IRestoreEngine? _engine;
    private RestoreParameterSet? _parameters;
    private LogShippingSession? _logShippingSession;

    public AppSettings Settings { get; private set; }

    public static IReadOnlyList<ExistingDbActionOption> ExistingDbActionOptions { get; } =
    [
        new(ExistingDatabaseAction.DoNothing, "Fail if database exists (safe)"),
        new(ExistingDatabaseAction.KillConnections, "Kill active connections & overwrite"),
        new(ExistingDatabaseAction.TakeOffline, "Take offline, then overwrite"),
        new(ExistingDatabaseAction.Drop, "Drop database, then restore")
    ];

    [ObservableProperty] private string _connectionStatusText = "Not connected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _engineName = "sp_DatabaseRestore";

    /// <summary>
    /// Avalonia's MenuItem.Header renders as AccessText, which treats a single '_' as an
    /// access-key marker (consuming it) - doubling it is the escape for a literal underscore,
    /// so "sp_DatabaseRestore" doesn't render as "spDatabaseRestore" in the toolbar menu.
    /// </summary>
    public string EngineMenuHeader => $"Engine: {EngineName.Replace("_", "__")}";

    public ObservableCollection<DatabaseInfo> Databases { get; } = new();
    [ObservableProperty] private DatabaseInfo? _selectedDatabase;

    [ObservableProperty] private string _targetDatabaseName = string.Empty;

    /// <summary>
    /// Ola Hallengren backup share root (e.g. \\192.168.178.15\SQLBackup). When set, this is the
    /// default source of truth for the three folders below - they are re-derived automatically
    /// whenever this or SelectedDatabase changes, via OlaHallengrenBackupPaths. Leave blank to
    /// fall back to manually browsing/typing each folder.
    /// </summary>
    [ObservableProperty] private string _backupRootPath = string.Empty;

    [ObservableProperty] private string _backupPathFull = string.Empty;
    [ObservableProperty] private string _backupPathDiff = string.Empty;
    [ObservableProperty] private string _backupPathLog = string.Empty;

    /// <summary>
    /// Non-null when any backup path uses a mapped drive letter (e.g. "W:\") - the SQL Server
    /// service account can't see those even though they work fine in Explorer for the DBA, which
    /// otherwise surfaces as a confusing "no backups found" error from the proc. See
    /// BackupPathDiagnostics for why.
    /// </summary>
    public string? MappedDriveWarning =>
        BackupPathDiagnostics.IsMappedDriveLetterPath(BackupPathFull) ||
        BackupPathDiagnostics.IsMappedDriveLetterPath(BackupPathDiff) ||
        BackupPathDiagnostics.IsMappedDriveLetterPath(BackupPathLog)
            ? "These backup paths use a mapped drive letter, which the SQL Server service account cannot see " +
              "even though it works in Explorer for you. Use the full UNC path instead (e.g. \\\\192.168.178.15\\SQLBackup\\...)."
            : null;

    [ObservableProperty] private BackupChainTimeline? _chain;
    [ObservableProperty] private DateTime? _selectedPointInTimeUtc;

    [ObservableProperty] private ExistingDbActionOption _selectedExistingDbAction = ExistingDbActionOptions[0];
    [ObservableProperty] private bool _runCheckDbAfterRestore;
    [ObservableProperty] private bool _moveFiles;
    [ObservableProperty] private string _moveDataDrive = string.Empty;
    [ObservableProperty] private string _moveLogDrive = string.Empty;
    [ObservableProperty] private bool _isLogShippingMode;

    [ObservableProperty] private string _sqlPreviewText = string.Empty;

    /// <summary>Two-line at-a-glance summary shown above the full generated script.</summary>
    [ObservableProperty] private string _planSummaryLine1 = "No restore planned yet.";
    [ObservableProperty] private string _planSummaryLine2 = string.Empty;

    /// <summary>
    /// The "-WhatIf" result: the actual low-level RESTORE DATABASE/RESTORE LOG statements resolved
    /// by a live, no-op dry run against the server (see IRestoreEngine.ResolveActualStatementsAsync).
    /// Cleared whenever the wrapper script changes, since a stale resolve could be misleading.
    /// </summary>
    [ObservableProperty] private string _resolvedStatementsText = string.Empty;
    [ObservableProperty] private bool _isResolvingStatements;
    [ObservableProperty] private int _resolvedStatementCount;

    public ObservableCollection<RestoreProgressEvent> ProgressLog { get; } = new();
    [ObservableProperty] private double? _percentComplete;

    public ObservableCollection<ParameterEditorRow> AdvancedRows { get; } = new();

    // Plain delegate properties (not C# events) - MainWindow assigns these once via '=' to wire up
    // dialog display, since there is exactly one subscriber (the View) for the ViewModel's lifetime.
    public Func<ConnectDialogViewModel, Task<ConnectionProfile?>>? ShowConnectDialog { get; set; }
    public Func<AboutDialogViewModel, Task>? ShowAboutDialog { get; set; }
    public Func<RestoreConfirmationDialogViewModel, Task<bool>>? ShowConfirmationDialog { get; set; }
    public Func<NewLogFoundDialogViewModel, Task<bool>>? ShowNewLogFoundDialog { get; set; }
    public Func<FileListPreviewDialogViewModel, Task>? ShowFileListDialog { get; set; }
    public Func<BackupTimelineDialogViewModel, Task>? ShowBackupTimelineDialog { get; set; }
    public Action<string>? ShowError { get; set; }

    public MainWindowViewModel()
    {
        Settings = _settingsService.Load();
        ApplyEngineFromSettings();
    }

    partial void OnEngineNameChanged(string value) => OnPropertyChanged(nameof(EngineMenuHeader));

    partial void OnSelectedDatabaseChanged(DatabaseInfo? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(TargetDatabaseName))
        {
            TargetDatabaseName = value.Name;
        }

        ApplyOlaFolderStructureIfRootSet();
        RefreshPreview();
    }

    partial void OnBackupRootPathChanged(string value)
    {
        if (_connectionProfile is not null && !string.IsNullOrWhiteSpace(value))
        {
            Settings.BackupRootPaths[_connectionProfile.ServerName] = value;
            _settingsService.Save(Settings);
        }

        ApplyOlaFolderStructureIfRootSet();
    }

    /// <summary>
    /// Derives Full/Diff/Log from BackupRootPath + the connected server + SelectedDatabase via the
    /// Ola Hallengren convention. This is the default path source - the three fields stay freely
    /// editable afterward for anyone whose layout doesn't match (hence not forced read-only).
    /// </summary>
    private void ApplyOlaFolderStructureIfRootSet()
    {
        if (string.IsNullOrWhiteSpace(BackupRootPath) || _connectionProfile is null || SelectedDatabase is null)
        {
            return;
        }

        var (full, diff, log) = OlaHallengrenBackupPaths.Build(BackupRootPath, _connectionProfile.ServerName, SelectedDatabase.Name);
        BackupPathFull = full;
        BackupPathDiff = diff;
        BackupPathLog = log;
    }

    partial void OnBackupPathFullChanged(string value)
    {
        OnPropertyChanged(nameof(MappedDriveWarning));
        RefreshPreview();
    }

    partial void OnBackupPathDiffChanged(string value)
    {
        OnPropertyChanged(nameof(MappedDriveWarning));
        RefreshPreview();
    }

    partial void OnBackupPathLogChanged(string value)
    {
        OnPropertyChanged(nameof(MappedDriveWarning));
        RefreshPreview();
    }
    partial void OnSelectedPointInTimeUtcChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(PointInTimeDisplay));
        RefreshPreview();
    }
    partial void OnSelectedExistingDbActionChanged(ExistingDbActionOption value) => RefreshPreview();
    partial void OnRunCheckDbAfterRestoreChanged(bool value) => RefreshPreview();
    partial void OnMoveFilesChanged(bool value) => RefreshPreview();
    partial void OnMoveDataDriveChanged(string value) => RefreshPreview();
    partial void OnMoveLogDriveChanged(string value) => RefreshPreview();
    partial void OnIsLogShippingModeChanged(bool value) => RefreshPreview();
    partial void OnTargetDatabaseNameChanged(string value) => RefreshPreview();

    private void ApplyEngineFromSettings()
    {
        _engine = Settings.RestoreEngine switch
        {
            RestoreEngineKind.RestoreGene => new RestoreGeneEngine(),
            _ => new DatabaseRestoreEngine(OpenSecondaryConnectionAsync)
        };
        EngineName = _engine.Name;
    }

    [RelayCommand]
    public void SwitchEngine(RestoreEngineKind kind)
    {
        Settings.RestoreEngine = kind;
        _settingsService.Save(Settings);
        ApplyEngineFromSettings();
        RefreshPreview();
    }

    private async Task<SqlConnection> OpenSecondaryConnectionAsync(CancellationToken ct)
    {
        if (_connectionProfile is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        return await _connectionService.OpenConnectionAsync(_connectionProfile, "master", ct);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var vm = new ConnectDialogViewModel(Settings.RecentServers.Select(s => s.ServerName));
        var profile = ShowConnectDialog is null ? null : await ShowConnectDialog(vm);
        if (profile is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _activeConnection?.Dispose();
            _activeConnection = await _connectionService.OpenConnectionAsync(profile, "master");
            _connectionProfile = profile;
            IsConnected = true;
            ConnectionStatusText = $"Connected to {profile.ServerName}";

            _settingsService.AddRecentServer(Settings, profile.ServerName, profile.AuthMode);
            _settingsService.Save(Settings);

            BackupRootPath = Settings.BackupRootPaths.TryGetValue(profile.ServerName, out var rememberedRoot)
                ? rememberedRoot
                : string.Empty;

            await CheckEngineAvailabilityAsync();

            Databases.Clear();
            foreach (var db in await _databaseCatalogService.GetDatabasesAsync(_activeConnection))
            {
                Databases.Add(db);
            }

#if DEBUG
            // Dev/test convenience only - auto-picks the sample database and backup root, and
            // names the restore target "<sourcedb>_<timestamp>" so repeated test runs never
            // collide with a database left over from a previous run. Compiled out of Release
            // builds entirely, so this has no effect on the app other DBAs actually use.
            SelectedDatabase ??= Databases.FirstOrDefault(d => string.Equals(d.Name, "StackOverflow2010", StringComparison.OrdinalIgnoreCase));

            if (SelectedDatabase is not null)
            {
                TargetDatabaseName = $"{SelectedDatabase.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            if (string.IsNullOrWhiteSpace(BackupRootPath))
            {
                BackupRootPath = @"\\192.168.178.15\SQLBackup\WIN10\StackOverflow2010";
            }
#endif
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ShowError?.Invoke($"Could not connect: {ex.Message}");
            IsConnected = false;
            ConnectionStatusText = "Not connected";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckEngineAvailabilityAsync()
    {
        if (_activeConnection is null)
        {
            return;
        }

        if (Settings.RestoreEngine == RestoreEngineKind.DatabaseRestore)
        {
            var installed = await DatabaseRestoreEngine.IsInstalledAsync(_activeConnection);
            if (!installed)
            {
                ShowError?.Invoke(
                    "sp_DatabaseRestore is not installed on this server. Run scripts\\sp_DatabaseRestore.sql against it first.");
                return;
            }

            var hasCommandExecute = await DatabaseRestoreEngine.IsCommandExecuteDependencyInstalledAsync(_activeConnection);
            if (!hasCommandExecute)
            {
                ShowError?.Invoke(
                    "sp_DatabaseRestore requires Ola Hallengren's dbo.CommandExecute, which was not found on this server. " +
                    "Install it from ola.hallengren.com before restoring.");
            }
        }
        else
        {
            var installed = await RestoreGeneEngine.IsInstalledAsync(_activeConnection);
            if (!installed)
            {
                ShowError?.Invoke(
                    "sp_RestoreGene is not installed on this server. It is not bundled with this app - " +
                    "install it yourself from restoregene.com, or switch back to sp_DatabaseRestore in Settings.");
            }
        }
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        if (ShowAboutDialog is not null)
        {
            await ShowAboutDialog(new AboutDialogViewModel());
        }
    }

    [RelayCommand]
    private async Task DiscoverChainAsync()
    {
        if (_activeConnection is null || SelectedDatabase is null || string.IsNullOrWhiteSpace(BackupPathFull))
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Point in time stays blank unless the user fills it in: blank = restore to latest,
            // and no STOPAT is emitted into the generated script.
            Chain = await DiscoverTimelineAsync();
            IsBusy = false;
            await ShowTimelineAsync();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ShowError?.Invoke($"Could not read backup chain: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<BackupChainTimeline> DiscoverTimelineAsync()
    {
        var query = new BackupChainQuery(
            SelectedDatabase!.Name,
            BackupPathFull,
            string.IsNullOrWhiteSpace(BackupPathDiff) ? null : BackupPathDiff,
            string.IsNullOrWhiteSpace(BackupPathLog) ? null : BackupPathLog);

        return await _engine!.DiscoverBackupChainAsync(_activeConnection!, query);
    }

    /// <summary>The timeline dialog's Refresh button: re-scan the folders; null (after an error message) on failure.</summary>
    private async Task<BackupChainTimeline?> RefreshChainAsync()
    {
        if (_activeConnection is null || SelectedDatabase is null)
        {
            return null;
        }

        try
        {
            Chain = await DiscoverTimelineAsync();
            return Chain;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ShowError?.Invoke($"Could not read backup chain: {ex.Message}");
            return null;
        }
    }

    /// <summary>Re-opens the timeline dialog on the already-discovered chain to (re)choose the point in time.</summary>
    [RelayCommand]
    private Task ChoosePointInTimeAsync() => ShowTimelineAsync();

    [RelayCommand]
    private void ClearPointInTime() => SelectedPointInTimeUtc = null;

    private async Task ShowTimelineAsync()
    {
        if (Chain is null || Chain.Entries.Count == 0 || SelectedDatabase is null || ShowBackupTimelineDialog is null)
        {
            return;
        }

        var dialogVm = new BackupTimelineDialogViewModel(SelectedDatabase.Name, Chain, SelectedPointInTimeUtc, RefreshChainAsync);
        await ShowBackupTimelineDialog(dialogVm);

        if (dialogVm.Applied)
        {
            SelectedPointInTimeUtc = dialogVm.SelectedPointInTime;
        }
    }

    /// <summary>Exact point in time shown on the Basic tab (the DatePicker could not show a time of day).</summary>
    public string PointInTimeDisplay => SelectedPointInTimeUtc is { } p
        ? p.ToString("yyyy-MM-dd HH:mm:ss")
        : "Latest available (no STOPAT)";

    [RelayCommand]
    private async Task PreviewFilesAsync(string? backupFilePath)
    {
        if (_activeConnection is null || string.IsNullOrWhiteSpace(backupFilePath))
        {
            return;
        }

        try
        {
            var files = await _fileInspector.GetFileListAsync(_activeConnection, backupFilePath);
            var vm = new FileListPreviewDialogViewModel { BackupFilePath = backupFilePath };
            foreach (var f in files)
            {
                vm.Files.Add(f);
            }

            if (ShowFileListDialog is not null)
            {
                await ShowFileListDialog(vm);
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ShowError?.Invoke($"Could not read file list: {ex.Message}");
        }
    }

    private RestoreRequest BuildRequest(bool executeImmediately) => new()
    {
        SourceDatabase = SelectedDatabase?.Name ?? string.Empty,
        TargetDatabase = TargetDatabaseName,
        BackupPathFull = BackupPathFull,
        BackupPathDiff = string.IsNullOrWhiteSpace(BackupPathDiff) ? null : BackupPathDiff,
        BackupPathLog = string.IsNullOrWhiteSpace(BackupPathLog) ? null : BackupPathLog,
        PointInTimeUtc = SelectedPointInTimeUtc,
        ExistingDatabaseAction = SelectedExistingDbAction.Value,
        RunCheckDbAfterRestore = RunCheckDbAfterRestore,
        MoveFiles = MoveFiles,
        MoveDataDrive = string.IsNullOrWhiteSpace(MoveDataDrive) ? null : MoveDataDrive,
        MoveLogDrive = string.IsNullOrWhiteSpace(MoveLogDrive) ? null : MoveLogDrive,
        ExecuteImmediately = executeImmediately,
        KeepInNoRecoveryForLogShipping = IsLogShippingMode
    };

    private void RefreshPreview()
    {
        if (_engine is null || SelectedDatabase is null || string.IsNullOrWhiteSpace(BackupPathFull))
        {
            SqlPreviewText = string.Empty;
            ResolvedStatementsText = string.Empty;
            ResolvedStatementCount = 0;
            PlanSummaryLine1 = "No restore planned yet.";
            PlanSummaryLine2 = string.Empty;
            return;
        }

        var request = BuildRequest(executeImmediately: false);
        _parameters = _engine.GetDefaultParameters(request);
        SqlPreviewText = _engine.BuildScript(request, _parameters);
        ResolvedStatementsText = string.Empty;
        ResolvedStatementCount = 0;
        UpdatePlanSummary(request);

        AdvancedRows.Clear();
        foreach (var row in ParameterEditorRowFactory.Build(_engine.DescribeParameters(), _parameters, RefreshPreviewFromAdvancedEdit))
        {
            AdvancedRows.Add(row);
        }
    }

    private void RefreshPreviewFromAdvancedEdit()
    {
        if (_engine is null || _parameters is null || SelectedDatabase is null)
        {
            return;
        }

        var request = BuildRequest(executeImmediately: false);
        SqlPreviewText = _engine.BuildScript(request, _parameters);
        ResolvedStatementsText = string.Empty;
        ResolvedStatementCount = 0;
        UpdatePlanSummary(request);
    }

    /// <summary>
    /// The "-WhatIf" action: connects to the server (no changes made) and resolves the actual
    /// low-level RESTORE DATABASE/RESTORE LOG statements for the current plan.
    /// </summary>
    [RelayCommand]
    private async Task ResolveActualStatementsAsync()
    {
        if (_activeConnection is null || _engine is null || _parameters is null || SelectedDatabase is null)
        {
            ShowError?.Invoke("Connect and pick a database/backup folder first.");
            return;
        }

        IsResolvingStatements = true;
        try
        {
            var request = BuildRequest(executeImmediately: false);
            var resolved = await _engine.ResolveActualStatementsAsync(_activeConnection, request, _parameters);
            ResolvedStatementCount = ResolvedStatementFormatter.CountStatements(resolved);
            ResolvedStatementsText = string.IsNullOrWhiteSpace(resolved)
                ? "(The server returned no RESTORE statements for this plan - check the backup folders/database name.)"
                : ResolvedStatementFormatter.Format(resolved);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ResolvedStatementsText = string.Empty;
            ResolvedStatementCount = 0;
            ShowError?.Invoke($"Could not resolve the actual statements: {ex.Message}");
        }
        finally
        {
            IsResolvingStatements = false;
        }
    }

    private void UpdatePlanSummary(RestoreRequest request)
    {
        PlanSummaryLine1 = request.SourceDatabase == request.TargetDatabaseOrDefault
            ? $"Restore {request.SourceDatabase} (in place)"
            : $"Restore {request.SourceDatabase} -> {request.TargetDatabaseOrDefault}";

        PlanSummaryLine2 = request.PointInTimeUtc is { } pointInTime
            ? $"Point in time: {pointInTime:yyyy-MM-dd HH:mm:ss}"
            : "Point in time: latest available";
    }

    [RelayCommand]
    private async Task RunRestoreAsync()
    {
        if (_activeConnection is null || _engine is null || SelectedDatabase is null || string.IsNullOrWhiteSpace(BackupPathFull))
        {
            return;
        }

        var isDestructive = SelectedExistingDbAction.Value != ExistingDatabaseAction.DoNothing;
        if (isDestructive)
        {
            var confirmVm = new RestoreConfirmationDialogViewModel
            {
                TargetDatabase = TargetDatabaseName,
                WarningText = SelectedExistingDbAction.Value switch
                {
                    ExistingDatabaseAction.KillConnections =>
                        $"This will forcibly disconnect all active users on '{TargetDatabaseName}' and overwrite it. This cannot be undone.",
                    ExistingDatabaseAction.TakeOffline =>
                        $"This will take '{TargetDatabaseName}' offline and overwrite it. This cannot be undone.",
                    ExistingDatabaseAction.Drop =>
                        $"This will DROP '{TargetDatabaseName}' outright and restore over it. This cannot be undone.",
                    _ => $"This will overwrite '{TargetDatabaseName}'. This cannot be undone."
                },
                SummaryText = SqlPreviewText
            };

            var confirmed = ShowConfirmationDialog is not null && await ShowConfirmationDialog(confirmVm);
            if (!confirmed)
            {
                return;
            }
        }

        IsBusy = true;
        ProgressLog.Clear();
        PercentComplete = null;
        try
        {
            var request = BuildRequest(executeImmediately: true);
            var parameters = _engine.GetDefaultParameters(request);

            var progress = new Progress<RestoreProgressEvent>(e =>
            {
                ProgressLog.Add(e);
                if (e.PercentComplete is { } pct)
                {
                    PercentComplete = pct;
                }
            });

            await _executionService.ExecuteAsync(
                _engine, _activeConnection, _connectionProfile!.ServerName, request, parameters, progress);

            if (!string.IsNullOrWhiteSpace(BackupPathLog) && IsLogShippingMode)
            {
                _logShippingSession = new LogShippingSession(
                    (DatabaseRestoreEngine)_engine, request, SelectedPointInTimeUtc ?? DateTime.UtcNow);
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ProgressLog.Add(new RestoreProgressEvent(DateTime.UtcNow, ex.Message, RestoreProgressSeverity.Error));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckForNewLogsAsync()
    {
        if (_logShippingSession is null || _activeConnection is null)
        {
            ShowError?.Invoke("Start a log-shipping restore first (check \"Log shipping mode\" and run an initial restore).");
            return;
        }

        try
        {
            var newLogs = await _logShippingSession.CheckForNewLogsAsync(_activeConnection);
            foreach (var log in newLogs)
            {
                var vm = new NewLogFoundDialogViewModel
                {
                    DatabaseName = SelectedDatabase?.Name ?? string.Empty,
                    LogFilePath = log.FilePath,
                    BackupFinishDateUtc = log.BackupFinishDate
                };

                var apply = ShowNewLogFoundDialog is not null && await ShowNewLogFoundDialog(vm);
                if (!apply)
                {
                    continue;
                }

                var progress = new Progress<RestoreProgressEvent>(e =>
                {
                    ProgressLog.Add(e);
                    if (e.PercentComplete is { } pct)
                    {
                        PercentComplete = pct;
                    }
                });

                await _logShippingSession.ApplyLogAsync(_activeConnection, log, progress);
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ShowError?.Invoke($"Could not check/apply new log backups: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task FinalizeLogShippingAsync()
    {
        if (_logShippingSession is null || _activeConnection is null)
        {
            return;
        }

        try
        {
            var progress = new Progress<RestoreProgressEvent>(e => ProgressLog.Add(e));
            await _logShippingSession.RecoverNowAsync(_activeConnection, progress);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            ShowError?.Invoke($"Could not finalize the log-shipping restore: {ex.Message}");
            return;
        }

        _logShippingSession = null;
    }
}
